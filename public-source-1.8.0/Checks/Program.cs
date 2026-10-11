using System;
using RandomMapStudio;
class Program
{
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Main()
 {
  bool initialJapanese=L.Japanese;
  L.Select(0,false);Assert(L.T("日本語","English")=="日本語","Japanese selection failed");
  L.Select(1,false);Assert(L.T("日本語","English")=="English","English selection failed");
  try{Settings.Parse("50","0","0",false);throw new Exception("Invalid sum accepted");}
  catch(ArgumentException e){Assert(e.Message.Contains("total 100%"),"Validation error not localized");}
  L.Select(initialJapanese?0:1,false);
  foreach(string bad in new[]{"NaN","Infinity","-1","101","abc"})
  {bool rejected=false;try{Settings.Parse(bad,"30","20",false);}catch(ArgumentException){rejected=true;}Assert(rejected,"Invalid input accepted: "+bad);}
  bool invalid=false;try{Settings.Parse("50","20","20",false);}catch(ArgumentException){invalid=true;}Assert(invalid,"Invalid sum accepted");
  var settings=Settings.Parse("50","30","20",false);
  var a=Generator.Generate(settings,12345,()=>false);var b=Generator.Generate(settings,12345,()=>false);
  int sea=0,mountains=0;
  for(int i=0;i<a.Heights.Length;i++){Assert(a.Heights[i]==b.Heights[i],"Nondeterministic terrain");if(a.Heights[i]<40*64)sea++;else if(a.Heights[i]>=120*64)mountains++;}
  Assert(sea==(int)Math.Round(a.Heights.Length*.3),"Sea area mismatch");Assert(mountains==(int)Math.Round(a.Heights.Length*.2),"Mountain area mismatch");
  var c=Generator.Generate(settings,9999,()=>false);bool different=false;for(int i=0;i<a.Heights.Length;i++)if(a.Heights[i]!=c.Heights[i]){different=true;break;}Assert(different,"Seeds produce identical terrain");
  settings.River=true;var river=Generator.Generate(settings,12345,()=>false);
  for(int z=river.RiverStart;z<=river.RiverEnd;z++){int x=(int)Math.Round(Map.RiverX(z/(double)(river.Size-1),river.RiverPhase)*(river.Size-1));Assert(river.Heights[z*river.Size+x]<40*64,"River disconnected");}
  for(int i=0;i<a.Heights.Length;i++)
   if(a.Heights[i]<40*64)Assert(a.Heights[i]==river.Heights[i],"Original sea/lake bed was carved");
  Assert(river.RiverStart>=0 && river.RiverEnd<river.Size,"Invalid river reach");
  CheckRiverEnds(river,a);
  for(int z=0;z<river.Size;z++)
  {
   int x=(int)Math.Round(Map.RiverX(z/(double)(river.Size-1),river.RiverPhase)*(river.Size-1));
   if(z<river.RiverStart || z>river.RiverEnd)Assert(a.Heights[z*river.Size+x]==river.Heights[z*river.Size+x],"River continued beyond mouth/source");
   else Assert(a.Heights[z*river.Size+x]>=40*64,"River crosses original water");
  }
  var ocean=Generator.Generate(Settings.Parse("0","100","0",true),12345,()=>false,257);
  Assert(!ocean.River && ocean.RiverCount==0,"River generated in all-water map");
  foreach(int index in ocean.RiverSourceIndices())throw new Exception("River source generated in ocean");
  foreach(var edge in new[]{Settings.Parse("100","0","0",false),Settings.Parse("0","100","0",false),Settings.Parse("0","0","100",false),Settings.Parse("0","50","50",false)})
  {var m=Generator.Generate(edge,7,()=>false);Assert(m.SeaCount==(int)Math.Round(m.Heights.Length*edge.Sea/100),"Edge sea mismatch");Assert(m.MountainCount==(int)Math.Round(m.Heights.Length*edge.Mountains/100),"Edge mountain mismatch");}
  foreach(int size in new[]{257,513,1201,2049})
  {
   var expanded=Generator.Generate(Settings.Parse("50","30","20",true),12345,()=>false,size);
   Assert(expanded.Size==size && expanded.Heights.Length==size*size,"Expanded dimensions mismatch");
   Assert(Map.DetectSize(expanded.Heights.Length)==size,"Grid detection mismatch");
   Assert(expanded.SeaCount==(int)Math.Round(size*(double)size*.3),"Expanded sea ratio mismatch");
   Assert(expanded.MountainCount==(int)Math.Round(size*(double)size*.2),"Expanded mountain ratio mismatch");
   Assert(expanded.WorldCoordinate(0)==-8640 && expanded.WorldCoordinate(size-1)==-8640+(size-1)*16,"Expanded world origin shifted");
   for(int z=expanded.RiverStart;z<=expanded.RiverEnd;z++)
   {int x=(int)Math.Round(Map.RiverX(z/(double)(size-1),expanded.RiverPhase)*(size-1));Assert(expanded.Heights[z*size+x]<=21*64,"River center too shallow");}
   int lastRow=-1;
   foreach(int index in expanded.RiverSourceIndices())
   {
    Assert(index>=0 && index<expanded.Heights.Length,"River source out of bounds");
    Assert(expanded.Heights[index]<=21*64,"River source not in deep channel");
    int row=index/size;Assert(lastRow<0?row==expanded.RiverStart:row-lastRow<=64,"River supply gap too large");lastRow=row;
   }
   Assert(lastRow==expanded.RiverEnd,"River end lacks supply");
  }
  foreach(int seed in new[]{1,7,12345,9999})
  foreach(var choice in new[]{Settings.Parse("50","30","20",false),Settings.Parse("100","0","0",false),Settings.Parse("10","80","10",false)})
  {
   var original=Generator.Generate(choice,seed,()=>false,257);
   choice.River=true;var connected=Generator.Generate(choice,seed,()=>false,257);
   if(connected.River)CheckRiverEnds(connected,original); else Assert(connected.RiverCount==0,"Unexpected river carving without a land reach");
  }
  bool malformed=false;try{Map.DetectSize(12345);}catch(ArgumentException){malformed=true;}Assert(malformed,"Malformed grid accepted");
  bool cancel=false;try{Generator.Generate(settings,1,()=>true);}catch(OperationCanceledException){cancel=true;}Assert(cancel,"Cancellation ignored");
  var depths=WaterFill.Depths(new ushort[]{0,20*64,39*64,40*64,41*64,800*64},40*64);
  var treeMap=new Map(3);
  for(int i=0;i<treeMap.Heights.Length;i++)treeMap.Heights[i]=80*64;
  treeMap.Heights[0]=treeMap.Heights[1]=treeMap.Heights[3]=treeMap.Heights[4]=20*64;
  Assert(treeMap.IsWaterAt(-8640,-8640)&&treeMap.IsWaterAt(-8632,-8632),"Water trees not selected");
  Assert(!treeMap.IsWaterAt(-8608,-8608),"Land trees selected");
  Assert(!treeMap.IsWaterAt(-8641,-8640)&&!treeMap.IsWaterAt(-8607,-8640),"Outside-map trees selected");
  treeMap.Heights[0]=40*64;Assert(!treeMap.IsWaterAt(-8640,-8640),"Shore-level tree selected");
  int expandedCode=(int)Math.Round(9800/0.263671875);
  Assert(Math.Abs(Map.ExpandedTreeAxis(unchecked((short)expandedCode),0x1000,0x1000)-9800)<.27,"Expanded tree coordinate decoded incorrectly");
  Assert(Map.ExpandedTreeAxis(-1000,0,0x1000)<0,"Ordinary negative tree coordinate moved");
  var outerTreeMap=new Map(1201);
  Assert(outerTreeMap.IsWaterAt(9800,9800),"Expanded water tree not detected");
  double outerHeight;Assert(outerTreeMap.TryGroundHeight(9800,9800,out outerHeight)&&outerHeight==0,"Expanded tree ground height wrong");
  Assert(depths[0]==40*64 && depths[1]==20*64 && depths[2]==64 && depths[3]==0 && depths[4]==0 && depths[5]==0,"Water depth must be relative to ground");
  foreach(int size in new[]{257,1081,1201,2049})
  {
   var ground=new ushort[size*size];for(int i=0;i<ground.Length;i++)ground[i]=41*64;
   ground[0]=20*64;ground[ground.Length-1]=39*64;
   var waterDepth=WaterFill.Depths(ground,40*64);var masks=WaterFill.Masks(waterDepth,size);
   int bits=(size+63)/64;
   Assert(masks[0]==1 && (masks[(size-1)/bits]&(1UL<<((size-1)/bits)))!=0,"Water mask misses outer edges");
   for(int i=0;i<ground.Length;i++)Assert(waterDepth[i]==0 || ground[i]+waterDepth[i]==40*64,"Initial water surface is not level");
  }
  Console.WriteLine("PASS: input validation, area ratios, deterministic seed, seed variation, connected river, 0/100% boundaries, cancellation, 257/513/1201/2049 grids, expanded river, preserved world origin, malformed grid rejection, original sea/lake preservation, river terminates at shore, all-water map has no river.");
 }
 static void CheckRiverEnds(Map river,Map original)
 {
  foreach(int row in new[]{river.RiverStart-1,river.RiverEnd+1})
  {
   if(row<0 || row>=river.Size)continue;
   int x=(int)Math.Round(Map.RiverX(row/(double)(river.Size-1),river.RiverPhase)*(river.Size-1));
   Assert(original.Heights[row*river.Size+x]<40*64,"River endpoint is stranded on land");
  }
  for(int row=river.RiverStart;row<=river.RiverEnd;row++)
  {
   int x=(int)Math.Round(Map.RiverX(row/(double)(river.Size-1),river.RiverPhase)*(river.Size-1));
   Assert(river.Heights[row*river.Size+x]<40*64,"Gap in connected river");
  }
 }
}

