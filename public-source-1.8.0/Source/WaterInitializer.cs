using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace RandomMapStudio
{
    // Use the game's load handshake to stop its separate water worker. A read
    // buffer is never modified while readers or the worker are using it.
    internal sealed class WaterInitializer : IDisposable
    {
        readonly WaterSimulation water;
        readonly object sync;
        readonly WaterSimulation.Cell[][] buffers;
        readonly ushort[] ground;
        readonly ulong[][] masks;
        readonly WaterSimulation.Status originalStatus;
        bool paused;
        static FieldInfo Field(string name)
        {
            var field = typeof(WaterSimulation).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (object.ReferenceEquals(field, null)) throw new InvalidOperationException(L.T("水面初期化に必要なゲーム内部フィールドがありません: ", "Required water field is missing: ") + name);
            return field;
        }
        object Get(string name) { return Field(name).GetValue(water); }
        void Set(string name, object value) { Field(name).SetValue(water, value); }
        internal WaterInitializer(WaterSimulation water, int length)
        {
            this.water = water;
            sync = Get("m_simulationBufferLock");
            buffers = (WaterSimulation.Cell[][])Get("m_waterBuffers");
            ground = (ushort[])Get("m_heightBuffer");
            masks = new[] { (ulong[])Get("m_waterExists1"), (ulong[])Get("m_waterExists2"), (ulong[])Get("m_waterExists3") };
            Field("m_bitCells"); Field("m_readLockCount"); Field("m_simulationStatus");
            if (ground.Length != length || buffers.Length != 2 || buffers[0].Length != length || buffers[1].Length != length)
                throw new InvalidOperationException(L.T("高さデータと水面グリッドのサイズが一致しません。", "Terrain and water grid sizes do not match."));
            foreach (var mask in masks) if (mask.Length != 64) throw new InvalidOperationException(L.T("未対応の水面マスクです。", "Unsupported water mask."));
            lock (sync)
            {
                originalStatus = (WaterSimulation.Status)Get("m_simulationStatus");
                if (originalStatus != WaterSimulation.Status.Running && originalStatus != WaterSimulation.Status.ReadyForLoad)
                    throw new InvalidOperationException(L.T("水シミュレーションの準備ができていません。", "Water simulation is not ready."));
                if (originalStatus == WaterSimulation.Status.Running)
                {
                    Set("m_simulationStatus", WaterSimulation.Status.WaitingForLoad); Monitor.PulseAll(sync);
                    var timer = Stopwatch.StartNew();
                    while ((WaterSimulation.Status)Get("m_simulationStatus") != WaterSimulation.Status.ReadyForLoad)
                    {
                        if (timer.ElapsedMilliseconds > 5000 || (WaterSimulation.Status)Get("m_simulationStatus") == WaterSimulation.Status.Terminated)
                        {
                            if ((WaterSimulation.Status)Get("m_simulationStatus") != WaterSimulation.Status.Terminated)
                                Set("m_simulationStatus", originalStatus);
                            Monitor.PulseAll(sync);
                            throw new InvalidOperationException(L.T("水シミュレーションの停止を待機できませんでした。", "Timed out waiting for the water worker to stop."));
                        }
                        Monitor.Wait(sync, 10);
                    }
                }
                paused = true;
            }
        }
        internal WaterSimulation.Cell[] Capture()
        {
            var read = water.BeginRead();
            try { return (WaterSimulation.Cell[])read.Clone(); }
            finally { water.EndRead(); }
        }
        internal void Publish(float seaLevel, WaterSimulation.Cell[] restored)
        {
            int size = Map.DetectSize(ground.Length);
            var depths = WaterFill.Depths(ground, (ushort)Math.Round(seaLevel * 64));
            var cells = new WaterSimulation.Cell[ground.Length];
            if (restored != null)
            {
                if (restored.Length != cells.Length) throw new InvalidOperationException(L.T("復元する水面サイズが一致しません。", "Water snapshot size does not match."));
                Array.Copy(restored, cells, cells.Length);
                for (int i = 0; i < depths.Length; i++) depths[i] = cells[i].m_height;
            }
            else for (int i = 0; i < cells.Length; i++) cells[i].m_height = depths[i];
            var mask = WaterFill.Masks(depths, size);
            lock (sync)
            {
                var timer = Stopwatch.StartNew();
                while ((int)Get("m_readLockCount") != 0)
                {
                    if (timer.ElapsedMilliseconds > 5000) throw new InvalidOperationException(L.T("水面読込の終了を待機できませんでした。", "Timed out waiting for water readers."));
                    Monitor.Wait(sync, 10);
                }
                if ((WaterSimulation.Status)Get("m_simulationStatus") != WaterSimulation.Status.ReadyForLoad)
                    throw new InvalidOperationException(L.T("水面の初期化中にシミュレーション状態が変わりました。", "Water simulation state changed during initialization."));
                foreach (var buffer in buffers) Array.Copy(cells, buffer, cells.Length);
                foreach (var target in masks) Array.Copy(mask, target, mask.Length);
                Set("m_bitCells", (size + 63) / 64);
                water.m_currentSeaLevel = water.m_nextSeaLevel = seaLevel;
                water.m_resetWater = false;
                // Rebuild render maps immediately, including while the game is paused.
                int patches = (int)Math.Sqrt(water.m_heightMaps.Length);
                if (patches * patches != water.m_heightMaps.Length) throw new InvalidOperationException(L.T("水面描画パッチが正方形ではありません。", "Water render patches do not form a square."));
                for (int z = 0; z < patches; z++) for (int x = 0; x < patches; x++) water.UpdatePatch(x, z);
            }
        }
        public void Dispose()
        {
            if (!paused) return;
            lock (sync)
            {
                if ((WaterSimulation.Status)Get("m_simulationStatus") == WaterSimulation.Status.ReadyForLoad)
                    Set("m_simulationStatus", originalStatus);
                Monitor.PulseAll(sync); paused = false;
            }
        }
    }
}
