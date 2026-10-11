using ColossalFramework;

namespace RandomMapStudio
{
    internal static class NetworkCleaner
    {
        internal static bool IsStructureNetwork(NetInfo info)
        {
            if (info == null || info.m_class == null) return false;
            return info.m_netAI is QuayAI || info.m_netAI is FloodWallAI || info.m_netAI is CanalAI ||
                info.m_netAI is DecorationWallAI || info.m_netAI is DamAI ||
                info.m_class.m_service == ItemClass.Service.Road ||
                info.m_class.m_subService == ItemClass.SubService.PublicTransportTrain ||
                info.m_class.m_subService == ItemClass.SubService.PublicTransportMetro ||
                info.m_class.m_subService == ItemClass.SubService.PublicTransportMonorail;
        }

        // Called only on the simulation thread. Manager APIs also release lanes,
        // zoning blocks, pillars and node buildings; never clear buffers directly.
        internal static int DeleteStructureNetworks()
        {
            var net = Singleton<NetManager>.instance;
            var nodes = new bool[net.m_nodes.m_buffer.Length];
            for (int i = 1; i < nodes.Length; i++)
                nodes[i] = (net.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) != 0 &&
                    IsStructureNetwork(net.m_nodes.m_buffer[i].Info);
            int removed = 0;
            for (int i = 1; i < net.m_segments.m_buffer.Length; i++)
            {
                var segment = net.m_segments.m_buffer[i];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0 || !IsStructureNetwork(segment.Info)) continue;
                nodes[segment.m_startNode] = nodes[segment.m_endNode] = true;
                net.ReleaseSegment((ushort)i, true);
                removed++;
            }
            for (int i = 1; i < nodes.Length; i++)
            {
                if (!nodes[i] || (net.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) == 0) continue;
                bool connected = false;
                for (int slot = 0; slot < 8; slot++)
                    if (net.m_nodes.m_buffer[i].GetSegment(slot) != 0) { connected = true; break; }
                if (!connected) net.ReleaseNode((ushort)i);
            }
            return removed;
        }

        internal static int DeleteBuildings()
        {
            var manager = Singleton<BuildingManager>.instance;
            int removed = 0;
            for (int i = 1; i < manager.m_buildings.m_buffer.Length; i++)
            {
                if ((manager.m_buildings.m_buffer[i].m_flags & Building.Flags.Created) == 0) continue;
                manager.ReleaseBuilding((ushort)i);
                removed++;
            }
            return removed;
        }
        internal static int DeleteWaterTrees(Map map)
        {
            var manager=Singleton<TreeManager>.instance;
            bool hundredTiles=false;
            foreach(var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                if(!object.ReferenceEquals(assembly.GetType("HundredTiles.SaveBridge.ObjectPositionHooks",false),null)){hundredTiles=true;break;}
            int removed=0;
            for(int i=1;i<manager.m_trees.m_buffer.Length;i++)
            {
                var tree=manager.m_trees.m_buffer[i];
                if((tree.m_flags & (ushort)TreeInstance.Flags.Created)==0)continue;
                var position=tree.Position;
                if(hundredTiles)
                {
                    position.x=(float)Map.ExpandedTreeAxis(tree.m_posX,tree.m_flags,0x1000);
                    position.z=(float)Map.ExpandedTreeAxis(tree.m_posZ,tree.m_flags,0x2000);
                }
                double height;
                if(!map.TryGroundHeight(position.x,position.z,out height))continue;
                if(height<40)
                {
                    manager.UpdateTreeRenderer((uint)i,true);
                    // ReleaseTree clears extension flags before FinalizeTree.
                    // Unlink the known expanded grid entry while the flags and
                    // world position are still available, avoiding a stale chain.
                    if(hundredTiles && (tree.m_flags&0x3000)!=0)
                        UnlinkExtendedTree(manager,(uint)i,position.x,position.z);
                    manager.ReleaseTree((uint)i);removed++;
                }
                else
                {
                    // Do not use Position's setter: preserve packed X/Z markers.
                    manager.m_trees.m_buffer[i].m_posY=(ushort)System.Math.Max(0,System.Math.Min(65535,System.Math.Round(height*64)));
                    manager.UpdateTree((uint)i);
                    manager.UpdateTreeRenderer((uint)i,true);
                }
            }
            int gridSide=(int)System.Math.Sqrt(manager.m_treeGrid.Length);
            int renderSide=gridSide/12;
            for(int z=0;z<renderSide;z++)for(int x=0;x<renderSide;x++)
                Singleton<RenderManager>.instance.UpdateGroup(x,z,manager.m_treeLayer);
            return removed;
        }
        static void UnlinkExtendedTree(TreeManager manager,uint id,float x,float z)
        {
            int side=(int)System.Math.Sqrt(manager.m_treeGrid.Length);
            if(side!=600 || side*side!=manager.m_treeGrid.Length)return;
            int gx=System.Math.Max(0,System.Math.Min(side-1,(int)System.Math.Floor((x-Map.WorldMin)/32)));
            int gz=System.Math.Max(0,System.Math.Min(side-1,(int)System.Math.Floor((z-Map.WorldMin)/32)));
            lock(manager.m_treeGrid)
            {
                uint previous=0,current=manager.m_treeGrid[gz*side+gx];
                for(int count=0;current!=0 && count<manager.m_trees.m_buffer.Length;count++)
                {
                    if(current==id)
                    {
                        uint next=manager.m_trees.m_buffer[current].m_nextGridTree;
                        if(previous==0)manager.m_treeGrid[gz*side+gx]=next;
                        else manager.m_trees.m_buffer[previous].m_nextGridTree=next;
                        manager.m_trees.m_buffer[current].m_nextGridTree=0;break;
                    }
                    previous=current;current=manager.m_trees.m_buffer[current].m_nextGridTree;
                }
            }
        }
    }
}
