using System;
using System.Text.Json;
using System.Diagnostics;
using Engine;
using Substrate;
using Substrate.Nbt;
using Substrate.Core;
using System.IO;
using System.Collections.Generic;
using Game;

public enum SCColor
{
    White,
    Pale_Cyan,
    Pink,
    Pale_Blue,
    Yellow,
    Pale_Green,
    Salmon,
    Light_Gray,
    Gray,
    Cyan,
    Purple,
    Blue,
    Brown,
    Green,
    Red,
    Black
}

public class JavaLevelConverter
{
    public TagNodeCompound Data;
    public ProjectSerializer project;
    
    public string WorldPath;
    
    public NbtTree Nbt(string path)
    {
        NBTFile nf = new NBTFile(path);
        NbtTree tree;
        using(Stream nbtstr = nf.GetDataInputStream())
        {
            tree = new NbtTree(nbtstr);
        }
        return tree;
    }
    
    public JavaLevelConverter(string worldPath, ProjectSerializer projectSerializer)
    {
        WorldPath = worldPath;
        NbtTree tree = Nbt(Path.Combine(WorldPath, "level.dat"));
        Data = ((TagNodeCompound) tree.Root).GetCompound("Data", false);
        project = projectSerializer;
    }
    
    public void Convert()
    {
        SetupGameInfo();
        SetupPlayers();
        ConvertChunks();
    }
    
    public void SetupGameInfo()
    {
         ProjectSerializer.Subsystem gameInfo = project.GetSubsystem("GameInfo");
        gameInfo.SetValue("WorldName", Data.GetText("LevelName", "Unnamed Minecraft world"));
        gameInfo.SetValue("GameMode", (GameMode) Data.GetInt("GameType", 1));
        gameInfo.SetValue("WorldSeedString", "From Minecraft (converted)");
        gameInfo.SetValue("OriginalSerializationVersion", "2.3");
        if (Data.ContainsKey("WorldGenSettings"))
        {
            TagNodeCompound world_gen = Data.GetCompound("WorldGenSettings", false);
            TagNodeCompound overworld = world_gen.GetCompound("dimensions", false).GetCompound("minecraft:overworld", true)?.GetCompound("generator", true);
            
            gameInfo.SetValue("WorldSeed", (int) world_gen.GetLong("seed", 0L));
            if (overworld != null)
            {
                gameInfo.SetValue("TerrainGenerationMode", overworld.GetText("type", "minecraft:noise") == "minecraft:flat" ? TerrainGenerationMode.FlatContinent : TerrainGenerationMode.Continent);
                gameInfo.SetValue("BiomeSize", overworld.GetText("settings", "minecraft:overworld") == "minecraft:large_biomes" ? 4f: 1f);
            }
        }
        else if (Data.ContainsKey("RandomSeed"))
        {
            gameInfo.SetValue("WorldSeed", (int) Data.GetLong("RandomSeed", 0L));
            gameInfo.SetValue("TerrainGenerationMode", Data.GetText("GeneratorName", "default") == "flat" ? TerrainGenerationMode.FlatContinent : TerrainGenerationMode.Continent);
            gameInfo.SetValue("BiomeSize", Data.GetText("GeneratorName", "default") == "largeBiomes" ? 4f: 1f);
        }
        else
        {
            gameInfo.SetValue("WorldSeed", 0);
            gameInfo.SetValue("TerrainGenerationMode", TerrainGenerationMode.Continent);
        }
        gameInfo.SetValue("EnvironmentBehaviorMode", (Data.GetCompound("GameRules", true)?.GetText("doMobSpawning", "true") != "false") ? EnvironmentBehaviorMode.Living : EnvironmentBehaviorMode.Static);
        gameInfo.SetValue("TotalElapsedGameTime", 0.05 * Data.GetLong("Time", 0L));
    }
    
    public void SetupPlayers()
    {
        ProjectSerializer.Subsystem players = project.GetSubsystem("Players");
        players.SetValue("NextPlayerIndex", 2);
        players.SetValue("GlobalSpawnPosition", new Engine.Vector3(Data.GetInt("SpawnX", 0), Data.GetInt("SpawnY", 256), Data.GetInt("SpawnZ", 0)));
        
        TagNodeCompound mainPlayer = Data.GetCompound("Player", true);
        if (mainPlayer == null)
            return;
        if (mainPlayer.GetText("Dimension", "minecraft:overworld") != "minecraft:overworld")
        {
            Console.WriteLine("Warning: The main player isn't in the overworld, the world won't have any player.");
            return;
        }
        
        Dictionary<string, object> playerData = new Dictionary<string, object>();
        playerData.Add("SpawnPosition", new Engine.Vector3(mainPlayer.GetInt("SpawnX", 0), mainPlayer.GetInt("SpawnY", 256), mainPlayer.GetInt("SpawnZ", 0)));
        playerData.Add("Level", ((float) mainPlayer.GetInt("XpLevel", 1)) + mainPlayer.GetFloat("XpP", 0f));
        playerData.Add("CharacterSkinName", "$Male1");
        playerData.Add("InputDevice", WidgetInputDevice.None);
        
        // Bruh Minecraft, I can't set any of these with the NBT data
        playerData.Add("FirstSpawnTime", 0.0);
        playerData.Add("LastSpawnTime", 0.0);
        playerData.Add("SpawnsCount", 0);
        playerData.Add("Name", "Steve");
        playerData.Add("PlayerClass", PlayerClass.Male);
        players.AddListValue("Players", playerData);
        
        ProjectSerializer.Entity playerEntity = project.MakeEntity("MalePlayer", "bef1b918-6418-41c9-a598-95e8ffd39ab3");
        ProjectSerializer.Component componentPlayer = playerEntity.GetComponent("Player");
        ProjectSerializer.Component componentBody = playerEntity.GetComponent("Body");
        ProjectSerializer.Component componentLocomotion = playerEntity.GetComponent("Locomotion");
        ProjectSerializer.Component componentHealth = playerEntity.GetComponent("Health");
        ProjectSerializer.Component componentOnFire = playerEntity.GetComponent("OnFire");
        
        componentPlayer.SetValue("PlayerIndex", 1);
        
        TagNodeList position = mainPlayer.GetList("Pos", true);
        if (position != null)
            componentBody.SetValue("Position", new Engine.Vector3((float) position[0].ToTagDouble().Data, (float) position[1].ToTagDouble().Data, (float) position[2].ToTagDouble().Data));
        
        componentLocomotion.SetValue("IsCreativeFlyEnabled", mainPlayer.GetCompound("abilities", true)?.GetByte("flying", 0) > 0);
        
        componentOnFire.SetValue("FireDuration", (mainPlayer.GetInt("Fire", 0) > 0) ? (float) mainPlayer.GetInt("Fire", 0) : 0f);
    }
    
    public bool MultithreadedTerrainConversion = true;
    
    public List<Point2> processedChunks = new List<Point2>();
    public List<string> unknownBlocks = new List<string>();
    
    public void ConvertChunks()
    {
        Console.WriteLine("Processing blocks...");
        Console.WriteLine("This may take a while.");
        
        List<Task> tasks = new List<Task>();
        using TerrainSerializer23 terrainSerializer = new TerrainSerializer23("tmp_sc");
        foreach (string regionName in Directory.GetFiles(Path.Combine(WorldPath, "region"))) 
        {
            string regionFilename = Path.GetFileName(regionName);
            if (!AnvilRegion.TestFileName(regionFilename))
            {
                continue;
            }
            
            if (MultithreadedTerrainConversion)
                tasks.Add(Task.Run(() => ConvertRegion(regionName, terrainSerializer)));
            else ConvertRegion(regionName, terrainSerializer);
        }
        if (MultithreadedTerrainConversion)
            /* Wait all tasks to end */                                                 foreach (Task task in tasks) task.Wait();
        Console.WriteLine();
        
        
        Console.WriteLine($"Blocks converted successfully. {processedChunks.Count} chunks have been converted, {unknownBlocks.Count} blocks couldn't be translated.");
        Console.WriteLine("Unknown blocks:");
        foreach (string blockName in unknownBlocks)
        {
            Console.Write(blockName + ", ");
        }
        Console.WriteLine();
    }
    
    private double processDuration_sum = 0.0;
    private double processDuration_count = 0.0;
    public int processingThreadCount = 0;
    
    public void ConvertRegion(string regionName, TerrainSerializer23 terrainSerializer)
    {
        string regionFilename = Path.GetFileName(regionName);
        
        int regionX;
        int regionZ;
        AnvilRegion.ParseFileName(regionFilename, out regionX, out regionZ);
        
        RegionFile region = new RegionFile(regionName);
        processingThreadCount++;
        TerrainChunk buffer_chunk = new TerrainChunk(0,0);
        for (int chunkX = 0; chunkX < 32; chunkX++)
        {
            for (int chunkZ = 0; chunkZ < 32; chunkZ++)
            {
                if (!region.HasChunk(chunkX, chunkZ))
                    continue;
                
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                bool chunkModified = false;
                
                // Get NBT tree
                NbtTree tree;
                using(Stream nbtstr = region.GetChunkDataInputStream(chunkX, chunkZ))
                {
                    tree = new NbtTree(nbtstr);
                }
                
                buffer_chunk.Coords = new Point2(tree.Root.GetInt("xPos", 0), tree.Root.GetInt("zPos", 0));
                buffer_chunk.Origin = new Point2(tree.Root.GetInt("xPos", 0), tree.Root.GetInt("zPos", 0)) * 16;
                Array.Clear(buffer_chunk.Cells);
                Array.Clear(buffer_chunk.Shafts);
                TagNodeList sections = tree.Root.GetList("sections", true);
                if (sections == null)
                    continue;
                foreach (TagNodeCompound section in sections)
                {
                    TagNodeCompound block_states = section.GetCompound("block_states", true);
                    TagNodeList paletteNode = block_states?.GetList("palette", true);
                     int sectionY = (int) section.GetByte("Y", 64);
                     if (sectionY < 0 || sectionY > 15 || block_states == null || paletteNode == null || !block_states.ContainsKey("data"))
                         continue;
                     
                     long[] blocks = block_states["data"].ToTagLongArray().Data;
                     
                     List<int> palette = new List<int>();
                     foreach (TagNodeCompound mc_block in paletteNode)
                     {
                         string blockName = mc_block.GetText("Name", "minecraft:air");
                         TagNodeCompound properties = mc_block.GetCompound("Properties", true);
                         int scblock = 4;
                         try
                         {
                             scblock = BlockTranslator[blockName];
                         }
                         catch (KeyNotFoundException)
                         {
                             if (!unknownBlocks.Contains(blockName))
                             {
                                unknownBlocks.Add(blockName);
                             }
                         }
                         scblock = ProcessBlockData(blockName, scblock, properties);
                         palette.Add(scblock);
                     }
                     
                     // Skip if it's just air
                     if (palette.Count == 1 && palette[0] == 0)
                     {
                         continue;
                     }
                     
                     int indice = 0;
                     for (int y = 0; y < 16; y++)
                     {
                         for (int z = 0; z < 16; z++)
                        {
                            for (int x = 0; x < 16; x++)
                            {
                                int blockId = palette[(int) ScaryCode(palette, blocks, indice)];
                                buffer_chunk.SetCellValueFast(x, sectionY * 16 + y, z, blockId);
                                indice++;
                            }
                        }
                    }
                    chunkModified=true;
                }
                buffer_chunk.Postcalculate();
                if (chunkModified)
                {
                    terrainSerializer.SaveChunkData(buffer_chunk);
                    processedChunks.Add(new Point2(buffer_chunk.Coords.X, buffer_chunk.Coords.Y));
                }
                stopwatch.Stop();
                if (chunkModified)
                {
                    processDuration_sum += stopwatch.Elapsed.TotalSeconds;
                    processDuration_count++;
                    
                    string chunksPerSec = (1.0 / (processDuration_sum/processDuration_count) * processingThreadCount).ToString("0.#");
                    
                    Console.Write($"\r{processedChunks.Count} chunks processed. {chunksPerSec} chunks/s");
                }
                
               if (tree.Root.ContainsKey("block_entities"))
               {
                   foreach (TagNodeCompound blockEntity in tree.Root.GetList("block_entities", false))
                   {
                       switch (blockEntity.GetText("id", "nothing"))
                       {
                           case "minecraft:sign":
                               Dictionary<string, object> signData = new Dictionary<string, object>();
                               TagNodeCompound front_text = blockEntity.GetCompound("front_text", true);
                               if (front_text == null)
                               {
                                   for (int i=1; i<=4; i++)
                                   {
                                       string text = GetSignText(blockEntity.GetText("Text"+i, ""));
                                       if (!string.IsNullOrEmpty(text))
                                       {
                                           signData["Line"+i] = text;
                                       }
                                     }
                                 }
                                 else
                                 {
                                     int textLine = 1;
                                     foreach (TagNode text in front_text.GetList("messages", true))
                                     {
                                         signData["Line"+textLine] = GetSignText(text.ToTagString().ToString());
                                         textLine++;
                                         if (textLine > 4)
                                         {
                                             break;
                                         }
                                     }
                                 }
                               
                               if (signData.Count == 0)
                                   continue;
                               signData.Add("Point", new Point3(blockEntity.GetInt("x", 0), blockEntity.GetInt("y", 0), blockEntity.GetInt("z", 0)));
                               
                               project.GetSubsystem("SignBlockBehavior").AddListValue("Texts", signData);
                               break;
                           case "minecraft:chest":
                           case "minecraft:barrel":
                               ProjectSerializer.Entity chestEntity = project.MakeEntity("Chest", "08550017-af17-4955-81fa-aafaf97b92bd");
                               ProjectSerializer.Component chestBlockEntity = chestEntity.GetComponent("BlockEntity");
                               chestBlockEntity.SetValue("Coordinates", new Point3(blockEntity.GetInt("x", 0), blockEntity.GetInt("y", 0), blockEntity.GetInt("z", 0)));
                               
                               break;
                       }
                   }
               }
            }
        }
        processingThreadCount--;
    }
    
    public string GetSignText(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            using (JsonDocument jsonDocument = JsonDocument.Parse(json))
            {
                JsonElement root = jsonDocument.RootElement;
                if (root.TryGetProperty("text", out JsonElement textElement))
                {
                    return textElement.GetString();
                }
            }
        }
        catch
        {
        }
        return json;
    }
    
    public int ProcessBlockData(string name, int convertedId, TagNodeCompound properties)
    {
        string facing;
        switch (name)
        {
            case "minecraft:wall_torch":
                facing = properties.GetText("facing", "none");
                if (facing != "none")
                {
                    if (facing == "south")
                        return Terrain.ReplaceData(convertedId, 0);
                    if (facing == "east")
                        return Terrain.ReplaceData(convertedId, 1);
                    if (facing == "north")
                        return Terrain.ReplaceData(convertedId, 2);
                    if (facing == "west")
                        return Terrain.ReplaceData(convertedId, 3);
                }
                return Terrain.ReplaceData(convertedId, 4);
            case "minecraft:torch":
                return Terrain.ReplaceData(convertedId, 4);
            case "minecraft:oak_stairs":
            case "minecraft:sandstone_stairs":
            case "minecraft:brick_stairs":
            case "minecraft:birch_stairs":
            case "minecraft:stone_stairs":
            case "minecraft:blackstone_stairs":
            case "minecraft:purpur_stairs":
            case "minecraft:cobblestone_stairs":
            case "minecraft:spruce_stairs":
            case "minecraft:jungle_stairs":
            case "minecraft:quartz_stairs":
            case "minecraft:acacia_stairs":
            case "minecraft:warped_stairs":
            case "minecraft:red_sandstone_stairs":
            case "minecraft:dark_oak_stairs":
            case "minecraft:granite_stairs":
            case "minecraft:diorite_stairs":
            case "minecraft:crimson_stairs":
            case "minecraft:andesite_stairs":
            case "minecraft:smooth_sandstone_stairs":
            case "minecraft:cut_copper_stairs":
            case "minecraft:mossy_cobblestone_stairs":
            case "minecraft:stone_brick_stairs":
            case "minecraft:prismarine_stairs":
            case "minecraft:nether_brick_stairs":
            case "minecraft:smooth_red_sandstone_stairs":
            case "minecraft:polished_blackstone_stairs":
            case "minecraft:smooth_quartz_stairs":
            case "minecraft:end_stone_brick_stairs":
            case "minecraft:deepslate_tile_stairs":
            case "minecraft:dark_prismarine_stairs":
            case "minecraft:red_nether_brick_stairs":
            case "minecraft:deepslate_brick_stairs":
            case "minecraft:waxed_cut_copper_stairs":
            case "minecraft:prismarine_brick_stairs":
            case "minecraft:polished_granite_stairs":
            case "minecraft:mossy_stone_brick_stairs":
            case "minecraft:polished_diorite_stairs":
            case "minecraft:polished_blackstone_brick_stairs":
            case "minecraft:polished_andesite_stairs":
            case "minecraft:cobbled_deepslate_stairs":
            case "minecraft:exposed_cut_copper_stairs":
            case "minecraft:oxidized_cut_copper_stairs":
            case "minecraft:polished_deepslate_stairs":
            case "minecraft:weathered_cut_copper_stairs":
            case "minecraft:waxed_exposed_cut_copper_stairs":
            case "minecraft:waxed_oxidized_cut_copper_stairs":
            case "minecraft:waxed_weathered_cut_copper_stairs":
                facing = properties.GetText("facing", "none");
                bool isUpsideDown = properties.GetText("half", "bottom") == "top";
              /*  if (convertedId & 0x1F)
                    throw new Exception("Star block out of range: "+convertedId);*/
                if (facing != "none" && convertedId != 4)
                {
                    if (facing == "south")
                        return SetIsUpsideDown(SetStairRotation(convertedId, 2), isUpsideDown);
                    if (facing == "east")
                        return SetIsUpsideDown(SetStairRotation(convertedId, 3), isUpsideDown);
                    if (facing == "north")
                        return SetIsUpsideDown(SetStairRotation(convertedId, 0), isUpsideDown);
                    if (facing == "west")
                        return SetIsUpsideDown(SetStairRotation(convertedId, 1), isUpsideDown);
                }
                break;
        }
        if (convertedId == 31)
        {
            Console.WriteLine(name);
        }
        return convertedId;
    }
    
    
    // No idea how this works... What even is this?
    public static ulong ScaryCode(List<int> palette, long[] states, int index)
    {
        int bits = Math.Max(BitLength(palette.Count - 1), 4);
        
        int state = index / (64 / bits);
        
        ulong data = (ulong) states[state];
        
        ulong shifted_data;
        shifted_data = data >> (index % (64 / bits) * bits);
        
        return shifted_data & ((ulong)Math.Pow(2, bits) - 1);
    }
    
    // Same thing, for earlier versions (earlier than the 20w17a snapshot)
    public static ulong BeforeScaryCode(List<int> palette, long[] states, int index)
    {
        int bits = Math.Max(BitLength(palette.Count - 1), 4);
        
        int state = index * bits / 64;
        
        ulong data = (ulong) states[state];
        
        ulong shifted_data = data >> ((bits * index) % 64);
        
        if (64 - ((bits * index) % 64) < bits)
        {
            data = (ulong) states[state + 1];
            int leftover = (int) (bits - (Math.Pow((state + 1), 64 % bits))) % bits;
            shifted_data = ((data & (ulong) Math.Pow(2, leftover - 1)) << (bits - leftover)) |  (ulong) shifted_data;
        }
        ulong result = shifted_data & ((ulong)Math.Pow(2, bits) - 1);
        if (result < (ulong) palette.Count)
            return result;
        return 0L;
    }
    
    public static int BitLength(int num)
    {
          return (int) (Math.Log(num, 2)) + 1;
    }
    
    
    // Block data utils
    public static int Paint(int block, SCColor color)
    {
        int data = Terrain.ExtractData(block);
        data = (data & -32) | 1 | (((int) color) << 1);
        return Terrain.ReplaceData(block, data);
    }
    
    
    public static int SetIsTop(int block)
    {
        return Terrain.ReplaceData(block, Terrain.ExtractData(block) | 0x10);
    }
    
    //Fence
    public static int SetVariant(int block, int variant)
    {
        int data = Terrain.ExtractData(block);
        return Terrain.ReplaceData(block, (data & -0x10) | (variant & 0xF));
    }
    
    public static bool IsFence(int value)
    {
        int contents = Terrain.ExtractContents(value);
        return contents == 94 || contents == 163 || contents == 164 || contents == 166 || contents == 193 || contents == 194 || contents == 202;
    }
    
    public static int SetStairRotation(int block, int rotation)
    {
        return Terrain.ReplaceData(block, (Terrain.ExtractData(block) & -4) | (rotation & 3));
    }
    
    public static int SetIsUpsideDown(int block, bool isUpsideDown)
    {
        if (isUpsideDown)
        {
            return Terrain.ReplaceData(block, Terrain.ExtractData(block) | 4);
        }
        return Terrain.ReplaceData(block, Terrain.ExtractData(block) & -5);
    }
    
    public static int PaintStair(int block, SCColor color)
	{
		return Terrain.ReplaceData(block, (Terrain.ExtractData(block) & -993) | 0x20 | (((int) color & 0xF) << 6));
	}
    
    
    public enum GameMode
    {
        Survival=0,
        Creative=1,
        Adventure=2
    }
    
    public enum EnvironmentBehaviorMode
    {
        Living,
        Static
    }
    public enum TerrainGenerationMode
    {
        Continent,
        FlatContinent
    }
    
    public enum PlayerClass
    {
        Male,
        Female
    }
    
    public enum WidgetInputDevice
    {
        None,
	    Keyboard,
	    Mouse,
	    MultiMouse1,
	    MultiMouse2,
	    MultiMouse3,
	    MultiMouse4,
	    Touch,
	    GamePad1,
	    GamePad2,
	    GamePad3,
	    GamePad4,
	    VrControllers,
	    MultiKeyboards,
	    MultiMice,
	    Gamepads,
	    All
    }
    
    public static Dictionary<string,int> BlockTranslator = new Dictionary<string,int>()
    {
        {"minecraft:air", 0},
        { "minecraft:stone", 3},
        { "minecraft:diorite", 3},
        { "minecraft:granite", 3},
        { "minecraft:deepslate", 67},
        { "minecraft:grass", 19},
        { "minecraft:grass_block", 8},
        { "minecraft:dirt", 2},
        { "minecraft:cobblestone", 5},
        { "minecraft:mossy_cobblestone", 5},
        { "minecraft:wood_plank", 21},
        { "minecraft:bedrock", 1},
        { "minecraft:water", 18},
        { "minecraft:stationary_water", 18},
        { "minecraft:lava", 92},
        { "minecraft:stationary_lava", 92},
        { "minecraft:sand", 7},
        { "minecraft:gravel", 6},
        { "minecraft:oak_log", 9},
        { "minecraft:birch_log", 10},
        { "minecraft:spruce_log", 11},
        { "minecraft:dark_oak_log", 11},
        { "minecraft:jungle_log", 11},
        { "minecraft:oak_leaves", 12},
        { "minecraft:birch_leaves", 13},
        { "minecraft:spruce_leaves", 14},
        { "minecraft:dark_oak_leaves", 14},
        { "minecraft:jungle_leaves", 12},
        { "minecraft:leaves", 12},
        { "minecraft:sponge", 72},
        { "minecraft:glass", 15},
        { "minecraft:dispenser", 216},
        { "minecraft:sandstone", 4},
        { "minecraft:note_block", 183},
        { "minecraft:bed", 55},
        { "minecraft:sticky_piston", 237},
        { "minecraft:tall_grass", 19},
        { "minecraft:piston", 237},
        { "minecraft:piston_head", 238},
        { "minecraft:gold_block", 47},
        { "minecraft:iron_block", 46},
        { "minecraft:oak_slab", 55},
        { "minecraft:birch_slab", 55},
        { "minecraft:dark_oak_slab", Paint(55, SCColor.Brown)},
        { "minecraft:stone_slab", 53},
        { "minecraft:smooth_stone_slab", 54},
        { "minecraft:quartz_slab", 70},
        { "minecraft:brick_block", 73},
        { "minecraft:brick_wall", 73 },
        { "minecraft:tnt", 107},
        { "minecraft:obsidian", Paint(72, SCColor.Black)},
        { "minecraft:torch", 31},
        { "minecraft:wall_torch", 31},
        { "minecraft:fire", 104},
        { "minecraft:chest", 45},
        { "minecraft:redstone_wire", 276613},
        { "minecraft:diamond_block", 126},
        { "minecraft:crafting_table", 27},
        { "minecraft:crops", 119},
        { "minecraft:farmland", 168},
        { "minecraft:furnace", 64},
        { "minecraft:burning_furnace", 65},
        { "minecraft:sign_post", 97},
        { "minecraft:ladder", 59},
        { "minecraft:wall_sign", 98},
        { "minecraft:lever", 141},
        { "minecraft:stone_plate", 144},
        { "minecraft:iron_door", 57},
        { "minecraft:wood_plate", 144},
        { "minecraft:snow", 61},
        { "minecraft:ice", 62},
        { "minecraft:snow_block", Paint(72, SCColor.White) },
        { "minecraft:cactus", 127},
        { "minecraft:jack_o_lantern", 132},
        { "minecraft:redstone_repeater_off", 145},
        { "minecraft:redstone_repeater_on", 145},
        { "minecraft:locked_chest", 21},
        { "minecraft:trapdoor", 83},
        { "minecraft:iron_bars", 193 },
        { "minecraft:vine", 197},
        { "minecraft:fence_gate", 166},
        { "minecraft:end_portal_frame", 4},
        { "minecraft:redstone_lamp_off", 231},
        { "minecraft:redstone_lamp_on", 17},
        { "minecraft:command_block", 186},
        { "minecraft:wood_button", 142},
        { "minecraft:trapped_chest", 45},
        { "minecraft:light_weighted_pressure_plate", 144},
        { "minecraft:weighted_pressure_plate_heavy", 144},
        { "minecraft:redstone_comparator_inactive", 186},
        { "minecraft:redstone_comparator_active", 186},
        { "minecraft:daylight_sensor", 151},
        { "minecraft:carpet", 208},
        { "minecraft:iron_ore", 39},
        { "minecraft:coal_ore", 16},
        { "minecraft:redstone_ore", 148},
        { "minecraft:diamond_ore", 112},
        {"minecraft:gold_ore", 3},
        { "minecraft:coal_block", 150},
        { "minecraft:acacia_fence", 0},
        { "minecraft:acacia_planks", 21},
        { "minecraft:acacia_stairs", 49},
        { "minecraft:azure_bluet", 0},
        { "minecraft:bookshelf", 0},
        { "minecraft:brick_stairs", 0},
        { "minecraft:bricks", 73},
        { "minecraft:carrots", 0},
        { "minecraft:clay", 0},
        { "minecraft:cobblestone_stairs", 0},
        { "minecraft:cobblestone_wall", 0},
        { "minecraft:cobweb", 0},
        { "minecraft:dandelion", 0},
        { "minecraft:dark_oak_stairs", 0},
        { "minecraft:emerald_ore", 0},
        { "minecraft:fern", 0},
        { "minecraft:glass_pane", 0},
        { "minecraft:glowstone", 17},
        { "minecraft:infested_stone", 0},
        { "minecraft:lapis_ore", 3},
        { "minecraft:lilac", 0},
        { "minecraft:oak_door", 56},
        { "minecraft:oak_fence", 94},
        { "minecraft:oak_planks", 21},
        { "minecraft:oak_stairs", 49 },
        { "minecraft:oak_wall_sign", 98},
        { "minecraft:oxeye_daisy", 0},
        { "minecraft:peony", 0},
        { "minecraft:poppy", 0},
        { "minecraft:potatoes", 0},
        { "minecraft:pumpkin", 131},
        { "minecraft:quartz_block", 68},
        { "minecraft:quartz_stairs", 69},
        { "minecraft:spruce_planks", 21},
        { "minecraft:stone_bricks", 26},
        { "minecraft:wheat", 174},
        { "minecraft:white_wool", Paint(4, SCColor.White) },
        { "minecraft:orange_wool", Paint(4, SCColor.Red) },
        { "minecraft:magenta_wool", Paint(4, SCColor.Pink) },
        { "minecraft:light_blue_wool", Paint(4, SCColor.Pale_Blue) },
        { "minecraft:yellow_wool", Paint(4, SCColor.Yellow) },
        { "minecraft:lime_wool", Paint(4, SCColor.Pale_Green) },
        { "minecraft:pink_wool", Paint(4, SCColor.Pink) },
        { "minecraft:gray_wool", Paint(4, SCColor.Gray) },
        { "minecraft:light_gray_wool", Paint(4, SCColor.Light_Gray) },
        { "minecraft:cyan_wool", Paint(4, SCColor.Cyan) },
        { "minecraft:purple_wool", Paint(4, SCColor.Purple) },
        { "minecraft:blue_wool", Paint(4, SCColor.Blue) },
        { "minecraft:brown_wool", Paint(4, SCColor.Brown) },
        { "minecraft:green_wool", Paint(4, SCColor.Green) },
        { "minecraft:red_wool", Paint(4, SCColor.Red) },
        { "minecraft:black_wool", Paint(4, SCColor.Black) },
        { "minecraft:white_concrete", Paint(72, SCColor.White) },
        { "minecraft:orange_concrete", Paint(72, SCColor.Red) },
        { "minecraft:magenta_concrete", Paint(72, SCColor.Pink) },
        { "minecraft:light_blue_concrete", Paint(72, SCColor.Pale_Blue) },
        { "minecraft:yellow_concrete", Paint(72, SCColor.Yellow) },
        { "minecraft:lime_concrete", Paint(72, SCColor.Pale_Green) },
        { "minecraft:pink_concrete", Paint(72, SCColor.Pink) },
        { "minecraft:gray_concrete", Paint(72, SCColor.Gray) },
        { "minecraft:light_gray_concrete", Paint(72, SCColor.Light_Gray) },
        { "minecraft:cyan_concrete", Paint(72, SCColor.Cyan) },
        { "minecraft:purple_concrete", Paint(72, SCColor.Purple) },
        { "minecraft:blue_concrete", Paint(72, SCColor.Blue) },
        { "minecraft:brown_concrete", Paint(72, SCColor.Brown) },
        { "minecraft:green_concrete", Paint(72, SCColor.Green) },
        { "minecraft:red_concrete", Paint(72, SCColor.Red) },
        { "minecraft:black_concrete", Paint(72, SCColor.Black) },
        { "minecraft:white_carpet", Paint(208, SCColor.White) },
        { "minecraft:orange_carpet", Paint(208, SCColor.Red) },
        { "minecraft:magenta_carpet", Paint(208, SCColor.Pink) },
        { "minecraft:light_blue_carpet", Paint(208, SCColor.Pale_Blue) },
        { "minecraft:yellow_carpet", Paint(208, SCColor.Yellow) },
        { "minecraft:lime_carpet", Paint(208, SCColor.Pale_Green) },
        { "minecraft:pink_carpet", Paint(208, SCColor.Pink) },
        { "minecraft:gray_carpet", Paint(208, SCColor.Gray) },
        { "minecraft:light_gray_carpet", Paint(208, SCColor.Light_Gray) },
        { "minecraft:cyan_carpet", Paint(208, SCColor.Cyan) },
        { "minecraft:purple_carpet", Paint(208, SCColor.Purple) },
        { "minecraft:blue_carpet", Paint(208, SCColor.Blue) },
        { "minecraft:brown_carpet", Paint(208, SCColor.Brown) },
        { "minecraft:green_carpet", Paint(208, SCColor.Green) },
        { "minecraft:red_carpet", Paint(208, SCColor.Red) },
        { "minecraft:black_carpet", Paint(208, SCColor.Black) },
        { "minecraft:white_stained_glass", 15 },
        { "minecraft:orange_stained_glass", 15 },
        { "minecraft:magenta_stained_glass", 15 },
        { "minecraft:light_blue_stained_glass", 15 },
        { "minecraft:yellow_stained_glass", 15 },
        { "minecraft:lime_stained_glass", 15 },
        { "minecraft:pink_stained_glass", 15 },
        { "minecraft:gray_stained_glass", 15 },
        { "minecraft:light_gray_stained_glass", 15 },
        { "minecraft:cyan_stained_glass", 15 },
        { "minecraft:purple_stained_glass", 15 },
        { "minecraft:blue_stained_glass", 15 },
        { "minecraft:brown_stained_glass", 15 },
        { "minecraft:green_stained_glass", 15 },
        { "minecraft:red_stained_glass", 15 },
        { "minecraft:black_stained_glass", 15 },
        { "minecraft:white_bed", Paint(55, SCColor.White) },
        { "minecraft:orange_bed", Paint(55, SCColor.Red) },
        { "minecraft:magenta_bed", Paint(55, SCColor.Pink) },
        { "minecraft:light_blue_bed", Paint(55, SCColor.Pale_Blue) },
        { "minecraft:yellow_bed", Paint(55, SCColor.Yellow) },
        { "minecraft:lime_bed", Paint(55, SCColor.Pale_Green) },
        { "minecraft:pink_bed", Paint(55, SCColor.Pink) },
        { "minecraft:gray_bed", Paint(55, SCColor.Gray) },
        { "minecraft:light_gray_bed", Paint(55, SCColor.Light_Gray) },
        { "minecraft:cyan_bed", Paint(55, SCColor.Cyan) },
        { "minecraft:purple_bed", Paint(55, SCColor.Purple) },
        { "minecraft:blue_bed", Paint(55, SCColor.Blue) },
        { "minecraft:brown_bed", Paint(55, SCColor.Brown) },
        { "minecraft:green_bed", Paint(55, SCColor.Green) },
        { "minecraft:red_bed", Paint(55, SCColor.Red) },
        { "minecraft:black_bed", Paint(55, SCColor.Black) },
        { "minecraft:white_terracotta", Paint(72, SCColor.White) },
        { "minecraft:orange_terracotta", Paint(72, SCColor.Red) },
        { "minecraft:magenta_terracotta", Paint(72, SCColor.Pink) },
        { "minecraft:light_blue_terracotta", Paint(72, SCColor.Pale_Blue) },
        { "minecraft:yellow_terracotta", Paint(72, SCColor.Yellow) },
        { "minecraft:lime_terracotta", Paint(72, SCColor.Pale_Green) },
        { "minecraft:pink_terracotta", Paint(72, SCColor.Pink) },
        { "minecraft:gray_terracotta", Paint(72, SCColor.Gray) },
        { "minecraft:light_gray_terracotta", Paint(72, SCColor.Light_Gray) },
        { "minecraft:cyan_terracotta", Paint(72, SCColor.Cyan) },
        { "minecraft:purple_terracotta", Paint(72, SCColor.Purple) },
        { "minecraft:blue_terracotta", Paint(72, SCColor.Blue) },
        { "minecraft:brown_terracotta", Paint(72, SCColor.Brown) },
        { "minecraft:green_terracotta", Paint(72, SCColor.Green) },
        { "minecraft:red_terracotta", Paint(72, SCColor.Red) },
        { "minecraft:black_terracotta", Paint(72, SCColor.Black) },
        { "minecraft:flowering_azalea", 0 },               { "minecraft:acacia_slab", 0 },                    { "minecraft:spruce_slab", 55 },
        { "minecraft:jungle_slab", 55 },
        { "minecraft:prismarine_slab", 0 },
        { "minecraft:dark_prismarine_slab", 0 },
        { "minecraft:sandstone_slab", 0 },
        { "minecraft:red_sandstone_slab", 0 },
        { "minecraft:smooth_sandstone_slab", 0 },
        { "minecraft:cut_sandstone_slab", 0 },
        { "minecraft:cobblestone_slab", 0 },
        { "minecraft:polished_granite_slab", 0 },
        { "minecraft:polished_diorite_slab", 0 },
        { "minecraft:andesite_slab", Paint(52, SCColor.Light_Gray) },
        { "minecraft:petrified_oak_slab", 55 },
        { "minecraft:brick_slab", 75 },
        { "minecraft:stone_brick_slab", 54 },
        { "minecraft:end_stone_brick_slab", Paint(54, SCColor.White) },
        { "minecraft:end_stone", Paint(95, SCColor.White) },
        { "minecraft:polished_blackstone_brick_slab", 0 },
        { "minecraft:deepslate_brick_slab", 0 },
        { "minecraft:nether_brick_slab", Paint(54, SCColor.Red) },
        { "minecraft:red_nether_brick_slab", Paint(54, SCColor.Red) },
        { "minecraft:waxed_oxidized_cut_copper_slab", 0 },
        { "minecraft:purpur_slab", Paint(54, SCColor.Purple) },
        { "minecraft:smooth_quartz_slab", 70 },
        { "minecraft:player_wall_head", 0 },
        { "minecraft:blue_orchid", 24 },
        { "minecraft:potted_blue_orchid", 0 },
        { "minecraft:structure_void", 0 },
        { "minecraft:brewing_stand", 0 },
        { "minecraft:jungle_wood", 9 },
        { "minecraft:oak_wood", 9 },
        { "minecraft:birch_wood", 10 },
        { "minecraft:spruce_wood", 9 },
        { "minecraft:acacia_wood", 9 },
        { "minecraft:dark_oak_wood", 11 },
        { "minecraft:stripped_dark_oak_wood", 9 },
        { "minecraft:end_rod", Paint(163, SCColor.White) },
        { "minecraft:blast_furnace", 64 },
        { "minecraft:spruce_fence", 94 },
        { "minecraft:jungle_fence", 94 },
        { "minecraft:birch_fence", 94 },
        { "minecraft:dark_oak_fence", Paint(94, SCColor.Brown) },
        { "minecraft:nether_brick_fence", 64 },
        { "minecraft:cake", 0 },
        { "minecraft:prismarine", Paint(67, SCColor.Purple) },
        { "minecraft:smooth_stone", 0 },
        { "minecraft:chiseled_sandstone", 0 },
        { "minecraft:red_sandstone", 0 },
        { "minecraft:chiseled_red_sandstone", 0 },
        { "minecraft:smooth_red_sandstone", 0 },
        { "minecraft:cut_red_sandstone", 0 },
        { "minecraft:cut_sandstone", 0 },
        { "minecraft:lodestone", 0 },
        { "minecraft:campfire", 209 },
        { "minecraft:tripwire", 0 },
        { "minecraft:nether_quartz_ore", 0 },
        { "minecraft:potted_wither_rose", 0 },
        { "minecraft:spruce_fence_gate", 0 },
        { "minecraft:jungle_fence_gate", 0 },
        { "minecraft:birch_fence_gate", 0 },
        { "minecraft:oak_fence_gate", 155 },
        { "minecraft:acacia_pressure_plate", 144 },
        { "minecraft:heavy_weighted_pressure_plate", 144 },
        { "minecraft:jungle_pressure_plate", 144 },
        { "minecraft:stone_pressure_plate", 144 },
        { "minecraft:oak_pressure_plate", 144 },
        { "minecraft:chiseled_deepslate", 67 },
        { "minecraft:polished_andesite", 68 },
        { "minecraft:acacia_log", 9 },
        { "minecraft:stripped_acacia_log", 9 },
        { "minecraft:stripped_spruce_log", 9 },
        { "minecraft:redstone_torch", 140 },
        { "minecraft:redstone_wall_torch", 140 },
        { "minecraft:rose_bush", 0 },
        { "minecraft:netherrack", Paint(67, SCColor.Red) },
        { "minecraft:magma_block", Paint(72, SCColor.Red) },
        { "minecraft:emerald_block", Paint(72, SCColor.Green) },
        { "minecraft:repeating_command_block", 0 },
        { "minecraft:slime_block", Paint(72, SCColor.Green) },
        { "minecraft:redstone_block", 138 },
        { "minecraft:structure_block", 0 },
        { "minecraft:netherite_block", 0 },
        { "minecraft:purpur_block", 0 },
        { "minecraft:lapis_block", 0 },
        { "minecraft:chiseled_quartz_block", 0 },
        { "minecraft:tripwire_hook", 0 },
        { "minecraft:barrel", 0 },
        { "minecraft:rail", 0 },
        { "minecraft:powered_rail", 0 },
        { "minecraft:detector_rail", 0 },
        { "minecraft:anvil", 0 },
        { "minecraft:sandstone_wall", Paint(202, SCColor.Yellow) },
        { "minecraft:red_sandstone_wall", 4 },
        { "minecraft:mossy_cobblestone_wall", 0 },
        { "minecraft:blackstone_wall", Paint(202, SCColor.Black) },
        { "minecraft:stone_brick_wall", 202 },
        { "minecraft:mossy_stone_brick_wall", 0 },
        { "minecraft:polished_blackstone_brick_wall", 0 },
        { "minecraft:deepslate_brick_wall", 0 },
        { "minecraft:red_nether_brick_wall", 0 },
        { "minecraft:podzol", 0 },
        { "minecraft:spore_blossom", 0 },
        { "minecraft:mycelium", 8 },
        { "minecraft:spruce_sign", 97 },
        { "minecraft:jungle_sign", 97 },
        { "minecraft:birch_sign", 97 },
        { "minecraft:oak_sign", 97 },
        { "minecraft:acacia_wall_sign", 0 },
        { "minecraft:birch_wall_sign", 0 },
        { "minecraft:dark_oak_wall_sign", 0 },
        { "minecraft:beacon", 0 },
        { "minecraft:cauldron", 0 },
        { "minecraft:water_cauldron", 0 },
        { "minecraft:acacia_button", 142 },
        { "minecraft:spruce_button", 142 },
        { "minecraft:jungle_button", 142 },
        { "minecraft:stone_button", 142 },
        { "minecraft:polished_blackstone_button", 142 },
        { "minecraft:birch_button", 142 },
        { "minecraft:oak_button", 142 },
        { "minecraft:dark_oak_button", 142 },
        { "minecraft:large_fern", 0 }, 
        { "minecraft:lantern", 17 },
        { "minecraft:sea_lantern", 17 },
        { "minecraft:soul_lantern", 17 },
        { "minecraft:kelp", 232 },
        { "minecraft:redstone_lamp", 0 },
        { "minecraft:quartz_pillar", 68 },
        { "minecraft:barrier", 0 },
        { "minecraft:smoker", 0 },
        { "minecraft:oxidized_cut_copper", 0 },
        { "minecraft:waxed_oxidized_cut_copper", 0 },
        { "minecraft:hopper", 0 },
        { "minecraft:dropper", 0 },
        { "minecraft:repeater", 224 },
        { "minecraft:composter", 0 },
        { "minecraft:cornflower", 0 },
        { "minecraft:sunflower", 0 },
        { "minecraft:respawn_anchor", 0 },
        { "minecraft:acacia_door", 56 },
        { "minecraft:spruce_door", 56 },
        { "minecraft:jungle_door", 56 },
        { "minecraft:birch_door", 56 },
        { "minecraft:dark_oak_door", 56 },
        { "minecraft:acacia_trapdoor", 83 },
        { "minecraft:jungle_trapdoor", 83 },
        { "minecraft:birch_trapdoor", 83 },
        { "minecraft:oak_trapdoor", 83 },
        { "minecraft:dark_oak_trapdoor", 83 },
        { "minecraft:iron_trapdoor", 84 },
        { "minecraft:comparator", 186 },
        { "minecraft:daylight_detector", 151 },
        { "minecraft:azalea_leaves", 12 },
        { "minecraft:flowering_azalea_leaves", 12 },
        { "minecraft:acacia_leaves", 12 },
        { "minecraft:cracked_stone_bricks", 26 },
        { "minecraft:chiseled_stone_bricks", 26 },
        { "minecraft:end_stone_bricks", Paint(26, SCColor.White) },
        { "minecraft:mossy_stone_bricks", 26 },
        { "minecraft:polished_blackstone_bricks", Paint(26, SCColor.Black) },
        { "minecraft:deepslate_bricks", Paint(26, SCColor.Black) },
        { "minecraft:nether_bricks", Paint(26, SCColor.Red) },
        { "minecraft:red_nether_bricks", Paint(26, SCColor.Red) },
        { "minecraft:quartz_bricks", 68 },
        { "minecraft:jungle_planks", 21 },
        { "minecraft:birch_planks", 21 },
        { "minecraft:dark_oak_planks", Paint(21, SCColor.Brown) },
        { "minecraft:warped_stairs", PaintStair(49, SCColor.Cyan) },
        { "minecraft:spruce_stairs", 49 },
        { "minecraft:jungle_stairs", 49 },
        { "minecraft:deepslate_tile_stairs", 96 },
        { "minecraft:prismarine_stairs", PaintStair(96, SCColor.Cyan) },
        { "minecraft:dark_prismarine_stairs", PaintStair(96, SCColor.Cyan) },
        { "minecraft:stone_stairs", 48 },
        { "minecraft:sandstone_stairs", 51 },
        { "minecraft:smooth_sandstone_stairs", 51 },
        { "minecraft:mossy_cobblestone_stairs", 48 },
        { "minecraft:polished_blackstone_stairs", PaintStair(48, SCColor.Black) },
        { "minecraft:polished_diorite_stairs", PaintStair(48, SCColor.White) },
        { "minecraft:polished_andesite_stairs", PaintStair(96, SCColor.Light_Gray) },
        { "minecraft:birch_stairs", 55 },
        { "minecraft:stone_brick_stairs", 50 },
        { "minecraft:mossy_stone_brick_stairs", 217 },
        { "minecraft:polished_blackstone_brick_stairs", 0 },
        { "minecraft:nether_brick_stairs", PaintStair(50, SCColor.Red) },
        { "minecraft:red_nether_brick_stairs", PaintStair(50, SCColor.Red) },
        { "minecraft:crimson_stairs", PaintStair(50, SCColor.Red) },
        { "minecraft:waxed_oxidized_cut_copper_stairs", PaintStair(69, SCColor.Red) },
        { "minecraft:purpur_stairs", PaintStair(69, SCColor.Purple) },
        { "minecraft:smooth_quartz_stairs", 69 },
        { "minecraft:seagrass", 233 },
        { "minecraft:kelp_plant", 232 },
        { "minecraft:flower_pot", 0 },
        { "minecraft:rooted_dirt", 2 },
        { "minecraft:coarse_dirt", 2 },
        { "minecraft:ender_chest", 45 }
    };
}