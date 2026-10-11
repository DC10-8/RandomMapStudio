using System;
using System.Collections;
using System.Threading;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnifiedUI.Helpers;
using UnityEngine;

namespace RandomMapStudio
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Random Map Studio"; } }
        public void OnSettingsUI(UIHelperBase helper)
        {
            helper.AddDropdown("Language / 言語",new[]{"日本語","English"},L.Japanese?0:1,delegate(int index){L.Select(index);});
        }
        public string Description { get { return L.T("新規ゲーム開始後、UUIから地形をプレビューして作成するMOD", "Preview and create random terrain through UUI after starting a new game."); } }
    }
    public class Loading : LoadingExtensionBase
    {
        GameObject host;
        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.NewGame) return;
            host = new GameObject("RandomMapStudio"); host.AddComponent<Runtime>();
        }
        public override void OnLevelUnloading() { if (host != null) UnityEngine.Object.Destroy(host); }
    }
    public class Runtime : MonoBehaviour
    {
        UIPanel panel; UITextField land, sea, mountains; UICheckBox river;
        UILabel status, details; UIButton generate, create, undo; UITextureSprite preview;
        Texture2D texture, icon; UUICustomButton uui;
        readonly object gate = new object(); readonly System.Random random = new System.Random();
        Map selected, completed; string failure;
        volatile bool disposed, applying; bool generating; int appliedSeed, removedNetworks, removedBuildings, removedTrees; string applyError;
        ushort[] previous; WaterSource[] previousSources; float previousSea; byte[] previousSand;
        WaterSimulation.Cell[] previousWater;
        Texture originalSandTexture,originalRuinedTexture; bool grassInsteadOfSand;
        bool uiJapanese;
        readonly System.Collections.Generic.Dictionary<Material,Texture[]> originalGroundTextures = new System.Collections.Generic.Dictionary<Material,Texture[]>();
        void SetGrassGround(bool enabled)
        {
            var terrain=Singleton<TerrainManager>.instance;
            if(enabled)
            {
                if(!grassInsteadOfSand)
                {
                    originalSandTexture=Shader.GetGlobalTexture("_TerrainSandDiffuse");
                    originalRuinedTexture=Shader.GetGlobalTexture("_TerrainRuinedDiffuse");
                    originalGroundTextures.Clear();
                }
                grassInsteadOfSand=true;
                var grass=terrain.m_properties.m_grassDiffuse;
                Shader.SetGlobalTexture("_TerrainSandDiffuse",grass);
                Shader.SetGlobalTexture("_TerrainRuinedDiffuse",grass);
                foreach(var material in new[]{terrain.m_terrainMaterial,terrain.m_decorationMaterial})
                {
                    if(material==null)continue;
                    if(!originalGroundTextures.ContainsKey(material))
                        originalGroundTextures.Add(material,new[]{material.HasProperty("_TerrainSandDiffuse")?material.GetTexture("_TerrainSandDiffuse"):null,material.HasProperty("_TerrainRuinedDiffuse")?material.GetTexture("_TerrainRuinedDiffuse"):null});
                    if(material.HasProperty("_TerrainSandDiffuse"))material.SetTexture("_TerrainSandDiffuse",grass);
                    if(material.HasProperty("_TerrainRuinedDiffuse"))material.SetTexture("_TerrainRuinedDiffuse",grass);
                }
            }
            else
            {
                grassInsteadOfSand=false;
                Shader.SetGlobalTexture("_TerrainSandDiffuse",originalSandTexture);
                Shader.SetGlobalTexture("_TerrainRuinedDiffuse",originalRuinedTexture);
                foreach(var pair in originalGroundTextures)
                {
                    if(pair.Key==null)continue;
                    if(pair.Key.HasProperty("_TerrainSandDiffuse"))pair.Key.SetTexture("_TerrainSandDiffuse",pair.Value[0]);
                    if(pair.Key.HasProperty("_TerrainRuinedDiffuse"))pair.Key.SetTexture("_TerrainRuinedDiffuse",pair.Value[1]);
                }
                originalGroundTextures.Clear();
            }
        }

        void Start()
        {
            try
            {
                BuildPanel(); icon = new Texture2D(40, 40, TextureFormat.RGBA32, false);
                for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
                    icon.SetPixel(x, y, y < 12 + 7 * Math.Sin(x * .17) ? new Color(.1f,.5f,.9f) :
                        y < 25 + 9 * Math.Sin(x * .12) ? new Color(.3f,.7f,.3f) : new Color(.8f,.8f,.8f));
                icon.Apply();
                uui = UUIHelpers.RegisterCustomButton("RandomMapStudio", null, L.T("ランダムマップ作成", "Random Map Studio"), icon,
                    delegate(bool active) { panel.isVisible = active; }, null, null);
            }
            catch (Exception ex) { Debug.LogError("[RandomMapStudio] " + ex); if (panel != null) { panel.Show(); status.text = L.T("UUI登録エラー: ", "UUI registration error: ") + ex.Message; } }
        }
        void BuildPanel()
        {
            uiJapanese=L.Japanese;
            panel = (UIPanel)UIView.GetAView().AddUIComponent(typeof(UIPanel));
            panel.size = new Vector2(780, 660); panel.relativePosition = new Vector3(100, 60); panel.backgroundSprite = "MenuPanel2";
            Label(L.T("ランダムマップ作成", "Random Map Studio"), 20, 15);
            var drag = panel.AddUIComponent<UIDragHandle>(); drag.size = new Vector2(720, 42); drag.target = panel;
            Button("×", 725, 8, 35, delegate { panel.Hide(); if (uui != null) uui.IsPressed = false; });
            land = Field(L.T("陸地 %", "Land %"), "50", 20); sea = Field(L.T("海 %", "Sea %"), "30", 190); mountains = Field(L.T("山地 %", "Mountains %"), "20", 360);
            river = panel.AddUIComponent<UICheckBox>(); river.relativePosition = new Vector3(550, 65); river.size = new Vector2(190, 26);
            // Both states share the same parent and explicit local position.
            var box = river.AddUIComponent<UISprite>(); box.spriteName = "check-unchecked"; box.size = new Vector2(20,20);
            box.relativePosition = new Vector3(0,3); box.isInteractive = false;
            var check = river.AddUIComponent<UISprite>(); check.spriteName = "check-checked"; check.size = box.size;
            check.relativePosition = box.relativePosition; check.isInteractive = false; check.isVisible = false;
            river.checkedBoxObject = check;
            river.label = river.AddUIComponent<UILabel>(); river.label.text = L.T("川を作る", "Add a river"); river.label.relativePosition = new Vector3(28, 2); river.label.textScale = .9f;
            Label(L.T("合計100％（山地は陸地と別枠）。川の面積は追加されます。", "Total: 100%. Mountains are separate from land. Rivers add water area."),20,110);
            preview = panel.AddUIComponent<UITextureSprite>(); preview.relativePosition = new Vector3(20,145); preview.size = new Vector2(448,448);
            Label(L.T("青: 海・川 / 緑: 陸地 / 灰: 山地", "Blue: water / Green: land / Gray: mountains"),490,150).textScale=.75f;
            details = Label(L.T("「生成」で候補を表示します。", "Click Generate to preview a candidate."),490,190); details.autoSize = false; details.size = new Vector2(265,180); details.wordWrap = true;
            generate = Button(L.T("生成", "Generate"),490,380,260,Generate);
            create = Button(L.T("作成", "Create"),490,430,260,delegate { Apply(false); }); create.isEnabled = false;
            undo = Button(L.T("元の地形に戻す", "Restore original terrain"),490,480,260,delegate { Apply(true); }); undo.isEnabled = false;
            var warning = Label(L.T("作成時に道路・線路・護岸・建物と水域の樹木を撤去。\n陸地の樹木は残します。\n地形を戻しても撤去した構造物・樹木は復元しません。", "Create removes roads, rail, quays, buildings and trees in water.\nLand trees remain.\nRestore does not recover removed objects."),490,535);
            warning.autoSize = false; warning.size = new Vector2(270,75); warning.wordWrap = true; warning.textScale = .75f;
            status = Label(L.T("新規ゲームの地形作成。街を建設する前に候補を選んでください。", "For new games. Choose your terrain before building a city."),20,615);
            status.autoSize = false; status.size = new Vector2(740,35); status.wordWrap = true; status.textScale = .8f;
            land.eventTextChanged += delegate { Invalidate(); }; sea.eventTextChanged += delegate { Invalidate(); };
            mountains.eventTextChanged += delegate { Invalidate(); }; river.eventCheckChanged += delegate { Invalidate(); };
            panel.Hide();
        }
        void Invalidate() { if (generating || applying) return; selected = null; create.isEnabled = false; status.text = L.T("設定を変更しました。「生成」で新しい候補を作ってください。", "Settings changed. Generate a new candidate."); }
        UILabel Label(string text, float x, float y) { var l = panel.AddUIComponent<UILabel>(); l.text = text; l.textScale = .9f; l.relativePosition = new Vector3(x,y); return l; }
        UITextField Field(string caption, string value, float x)
        {
            Label(caption,x,52); var f = panel.AddUIComponent<UITextField>(); f.relativePosition = new Vector3(x,76); f.size = new Vector2(140,28);
            f.normalBgSprite = "TextFieldPanel"; f.hoveredBgSprite = "TextFieldPanelHovered"; f.focusedBgSprite = "TextFieldPanel";
            f.text = value; f.padding = new RectOffset(6,6,4,4); f.textScale = .9f; f.maxLength = 12; f.selectOnFocus = true; return f;
        }
        UIButton Button(string text, float x, float y, float width, Action action)
        {
            var b = panel.AddUIComponent<UIButton>(); b.text = text; b.relativePosition = new Vector3(x,y); b.size = new Vector2(width,36);
            b.normalBgSprite = "ButtonMenu"; b.hoveredBgSprite = "ButtonMenuHovered"; b.pressedBgSprite = "ButtonMenuPressed";
            b.eventClicked += delegate { try { action(); } catch (Exception ex) { status.text = ex.Message; Debug.LogError(ex); } }; return b;
        }
        void Controls(bool enabled)
        {
            land.isEnabled = sea.isEnabled = mountains.isEnabled = river.isEnabled = generate.isEnabled = enabled;
            create.isEnabled = enabled && selected != null; undo.isEnabled = enabled && previous != null;
        }
        void Generate()
        {
            if (generating || applying) return;
            var settings = Settings.Parse(land.text,sea.text,mountains.text,river.isChecked); int seed = random.Next();
            int size = Map.DetectSize(Singleton<TerrainManager>.instance.RawHeights.Length);
            selected = null; generating = true; Controls(false); status.text = L.T("候補を生成しています…", "Generating a candidate...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { var map = Generator.Generate(settings, seed, delegate { return disposed; }, size); lock(gate) { if (!disposed) completed = map; } }
                catch (Exception ex) { lock(gate) { if (!disposed) failure = ex.Message; } }
            });
        }
        void Update()
        {
            if(panel!=null && uiJapanese!=L.Japanese)
            {
                string landValue=land.text,seaValue=sea.text,mountainValue=mountains.text;
                bool riverValue=river.isChecked,visible=panel.isVisible;var candidate=selected;
                var panelPosition=panel.relativePosition;
                Destroy(panel.gameObject);BuildPanel();
                land.text=landValue;sea.text=seaValue;mountains.text=mountainValue;river.isChecked=riverValue;
                selected=candidate;
                if(selected!=null)Draw(selected);else if(texture!=null)preview.texture=texture;
                Controls(!generating && !applying);panel.isVisible=visible;panel.relativePosition=panelPosition;
                if(uui!=null && uui.Button!=null)uui.Button.tooltip=L.T("ランダムマップ作成","Random Map Studio");
                status.text=generating?L.T("候補を生成しています…","Generating a candidate..."):
                    applying?L.T("地形と水面を反映しています…","Applying terrain and water..."):
                    L.T("言語を日本語に切り替えました。","Language switched to English.");
            }
            if(grassInsteadOfSand)SetGrassGround(true);
            Map map; string error; lock(gate) { map = completed; error = failure; completed = null; failure = null; }
            if (map != null) { generating = false; selected = map; Draw(map); Controls(true); status.text=L.T("候補を表示しました。「生成」で別候補、「作成」でゲーム内へ反映します。", "Candidate ready. Generate another, or Create to apply it in-game."); }
            if (error != null) { generating = false; Controls(true); status.text = L.T("生成エラー: ", "Generation error: ") + error; }
            if (!applying && applyPending)
            {
                applyPending = false;
                if(applyError==null && appliedSeed<0)selected=null;
                if(applyError==null)
                {
                    if(appliedSeed<0)
                    {
                        if(grassInsteadOfSand)SetGrassGround(false);
                    }
                    else
                    {
                        SetGrassGround(true);
                    }
                }
                Controls(true);
                status.text = applyError ?? (appliedSeed < 0 ? L.T("元の地形・水面に戻しました（撤去した構造物・樹木は復元されません）。", "Original terrain and water restored. Removed objects stay removed.") : L.T("作成完了。道路等", "Created. Removed networks: ")+removedNetworks+L.T("区間、建物", " / buildings: ")+removedBuildings+L.T("件、水域の樹木", " / water trees: ")+removedTrees+L.T("本を撤去しました。", "."));
            }
        }
        void Draw(Map map)
        {
            var next = new Texture2D(448,448,TextureFormat.RGBA32,false); var pixels = new Color32[448*448];
            for(int y=0;y<448;y++) for(int x=0;x<448;x++)
            {
                int gx=(int)Math.Round(x*(map.Size-1.0)/447),gz=(int)Math.Round(y*(map.Size-1.0)/447); float h=map.Heights[gz*map.Size+gx]/64f;
                pixels[y*448+x]=h<40 ? new Color(.08f,.25f+.006f*h,.65f+.004f*h) : h>=120 ? Color.Lerp(new Color(.42f,.4f,.35f),Color.white,(h-120)/680) : Color.Lerp(new Color(.22f,.55f,.22f),new Color(.65f,.7f,.35f),(h-40)/80);
            }
            next.SetPixels32(pixels); next.Apply(); preview.texture=next; if(texture!=null)Destroy(texture); texture=next;
            double n=map.Heights.Length; int water=0,mountain=0; foreach(ushort h in map.Heights) { if(h<40*64)water++; else if(h>=120*64)mountain++; }
            details.text=L.T("シード: ", "Seed: ")+map.Seed+L.T("\nサイズ: ", "\nSize: ")+map.WidthKm.ToString("F2")+L.T(" km四方\nグリッド: ", " km square\nGrid: ")+map.Size+"×"+map.Size+L.T("\n生成後の面積\n陸地: ", "\nGenerated area\nLand: ")+((n-water-mountain)*100/n).ToString("F1")+L.T(" %\n水域: ", " %\nWater: ")+(water*100/n).ToString("F1")+L.T(" %\n山地: ", " %\nMountains: ")+(mountain*100/n).ToString("F1")+L.T(" %\n川: ", " %\nRiver: ")+(map.River?L.T("あり", "Yes"):L.T("なし", "No"));
        }
        volatile bool applyPending;
        void Apply(bool restore)
        {
            if (generating || applying || (restore ? previous == null : selected == null)) return;
            applying=true; applyError=null; Controls(false); status.text=L.T("地形と水面を反映しています…", "Applying terrain and water...");
            Singleton<SimulationManager>.instance.AddAction(ApplyTerrain(restore,selected));
        }
        IEnumerator ApplyTerrain(bool restore, Map map)
        {
            WaterInitializer initializer=null;
            try
            {
                if(disposed)yield break;
                var terrain=Singleton<TerrainManager>.instance; var water=terrain.WaterSimulation;
                var target=restore?previous:map.Heights;
                if(terrain.RawHeights.Length!=target.Length)throw new InvalidOperationException(L.T("候補生成後に地形サイズが変わりました。もう一度「生成」してください。", "Terrain size changed. Generate another candidate."));
                initializer=new WaterInitializer(water,target.Length);
                var oldWater=initializer.Capture();
                var resources=Singleton<NaturalResourceManager>.instance;
                int resourceSide=Map.DetectSize(resources.m_naturalResources.Length);
                if(restore && (previousSand==null || previousSand.Length!=resources.m_naturalResources.Length))
                    throw new InvalidOperationException(L.T("砂地データのサイズが変わったため復元できません。", "Sand data size changed; cannot restore."));
                var oldSand=new byte[resources.m_naturalResources.Length];
                lock(resources.m_naturalResources)
                    for(int i=0;i<oldSand.Length;i++)oldSand[i]=resources.m_naturalResources[i].m_sand;
                var old=(ushort[])terrain.RawHeights.Clone(); float oldSea=water.m_nextSeaLevel;
                WaterSource[] sources;
                lock(water.m_waterSources)
                {
                    var natural=new System.Collections.Generic.List<WaterSource>();
                    for(int i=0;i<water.m_waterSources.m_size;i++)
                        if(water.m_waterSources.m_buffer[i].m_type==WaterSource.TYPE_NATURAL)natural.Add(water.m_waterSources.m_buffer[i]);
                    sources=natural.ToArray();
                }
                WaterSource[] targetSources=restore?previousSources:BuildSources(map);
                // Save before any mutation so a failed preview can also be undone.
                if(previous==null)
                {
                    previousWater=oldWater; previous=old; previousSources=sources;
                    previousSea=oldSea; previousSand=oldSand;
                }
                lock(water.m_waterSources)
                {
                    int facilities=0;
                    for(int i=0;i<water.m_waterSources.m_size;i++)
                        if(water.m_waterSources.m_buffer[i].m_type!=0 && water.m_waterSources.m_buffer[i].m_type!=WaterSource.TYPE_NATURAL)facilities++;
                    if(facilities+targetSources.Length>65535)throw new InvalidOperationException(L.T("水源の上限を超えました。", "Water source limit exceeded."));
                    water.m_waterSources.EnsureCapacity(Math.Min(65535,water.m_waterSources.m_size+targetSources.Length));
                    // Keep facility source IDs intact: buildings refer to these indices.
                    for(int i=water.m_waterSources.m_size-1;i>=0;i--)
                        if(water.m_waterSources.m_buffer[i].m_type==WaterSource.TYPE_NATURAL)water.ReleaseWaterSource((ushort)(i+1));
                    foreach(var source in targetSources)
                    {
                        ushort id;
                        if(!water.CreateWaterSource(out id,source))throw new InvalidOperationException(L.T("自然水源を作成できませんでした。", "Could not create a natural water source."));
                    }
                }
                // Preview and terrain undo must never delete networks.
                if(!restore)
                {
                    removedBuildings=NetworkCleaner.DeleteBuildings();
                    removedNetworks=NetworkCleaner.DeleteStructureNetworks();
                }
                Array.Copy(target,terrain.RawHeights,target.Length);
                // Sand is painted resource data, independent of height/water.
                // Remove the old map's shore pattern without changing ore/oil/etc.
                lock(resources.m_naturalResources)
                    for(int i=0;i<oldSand.Length;i++)resources.m_naturalResources[i].m_sand=restore?previousSand[i]:(byte)0;
                resources.AreaModified(0,0,resourceSide-1,resourceSide-1);
                resources.AreaModifiedB(0,0,resourceSide-1,resourceSide-1);
                int resolution=Map.DetectSize(target.Length)-1;
                for(int z=0;z<resolution;z+=120)for(int x=0;x<resolution;x+=120)
                    TerrainModify.UpdateArea(x,z,Math.Min(x+120,resolution),Math.Min(z+120,resolution),true,true,true);
                // The game normally eases BlockHeights toward BlockHeightTargets
                // over many frames. Water depths must use the final ground now,
                // otherwise new sea beds remain dry until this easing finishes.
                Array.Copy(terrain.BlockHeightTargets,terrain.BlockHeights,terrain.BlockHeights.Length);
                initializer.Publish(restore?previousSea:40f,restore?previousWater:null);
                if(!restore)removedTrees=NetworkCleaner.DeleteWaterTrees(map);
                appliedSeed=restore?-1:map.Seed;
            }
            catch(Exception ex) { applyError=L.T("反映エラー: ", "Apply error: ")+ex.Message; Debug.LogError("[RandomMapStudio] "+ex); }
            finally { if(initializer!=null)initializer.Dispose(); applying=false; applyPending=true; }
            yield break;
        }
        static WaterSource[] BuildSources(Map map)
        {
            // One natural source for each disconnected below-sea basin, including the river.
            var visited=new bool[map.Heights.Length]; var queue=new int[map.Heights.Length];
            var sources=new System.Collections.Generic.List<WaterSource>();
            for(int i=0;i<visited.Length;i++)
            {
                if(visited[i] || map.Heights[i]>=40*64)continue;
                int head=0,tail=1,lowest=i; queue[0]=i; visited[i]=true;
                while(head<tail)
                {
                    int p=queue[head++]; if(map.Heights[p]<map.Heights[lowest])lowest=p;
                    int x=p%map.Size,z=p/map.Size;
                    if(x>0)Visit(p-1,map,visited,queue,ref tail); if(x<map.Size-1)Visit(p+1,map,visited,queue,ref tail);
                    if(z>0)Visit(p-map.Size,map,visited,queue,ref tail); if(z<map.Size-1)Visit(p+map.Size,map,visited,queue,ref tail);
                }
                AddSource(sources,map,lowest);
            }
            // Long rivers need distributed supply, not just one basin source.
            foreach(int index in map.RiverSourceIndices())AddSource(sources,map,index);
            return sources.ToArray();
        }
        static void AddSource(System.Collections.Generic.List<WaterSource> sources,Map map,int index)
        {
            if(sources.Count>=65535)throw new InvalidOperationException(L.T("水源の上限を超えました。", "Water source limit exceeded."));
            var pos=new Vector3((float)map.WorldCoordinate(index%map.Size),map.Heights[index]/64f,(float)map.WorldCoordinate(index/map.Size));
            sources.Add(new WaterSource { m_type=1,m_inputPosition=pos,m_outputPosition=pos,m_target=40*64,m_inputRate=65535,m_outputRate=65535 });
        }
        static void Visit(int p,Map map,bool[] seen,int[] queue,ref int tail)
        { if(!seen[p] && map.Heights[p]<40*64){seen[p]=true;queue[tail++]=p;} }
        void OnDestroy()
        {
            if(grassInsteadOfSand)SetGrassGround(false);
            disposed=true; if(uui!=null)uui.Release(); if(panel!=null)Destroy(panel.gameObject);
            if(texture!=null)Destroy(texture); if(icon!=null)Destroy(icon);
        }
    }
}
