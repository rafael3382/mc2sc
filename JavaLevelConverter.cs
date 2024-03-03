using System;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        LoadTranslationDictionary();
    }

    public void LoadTranslationDictionary()
    {
        string hr2idJson = new StreamReader(typeof(JavaLevelConverter).Assembly.GetManifestResourceStream("WorldConverter.HumanReadable2Id.json")).ReadToEnd();
        string translationDictionaryJson = File.ReadAllText("BlockTranslation.json");
        var hr2id = JsonSerializer.Deserialize<Dictionary<string, int>>(hr2idJson);
        var translationDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(translationDictionaryJson);
        string[] colorNames = Enum.GetNames(typeof(SCColor));
        foreach (KeyValuePair<string, string> pair in translationDictionary)
        {
            if (pair.Value == "NoTranslation")
                continue;
            var match = Regex.Match(pair.Value, "([a-zA-Z]+)(?::([0-9]+))?(?: #([a-zA-Z_]+))?");
            if (!match.Success)
            {
                Console.WriteLine($"Error processing translation value \"{pair.Value}\". Skipping.");
                continue;
            }
            var groups = match.Groups.Values;
            int blockId = 0;
            string blockName;
            if (match.Groups.Count > 1)
                blockName = match.Groups[1].Value;
            else
                blockName = match.Groups[0].Value;
            try
            {
                blockId = hr2id[blockName];
            }
            catch
            {
                Console.WriteLine($"Warning: Couldn't translate the block name \"{blockName}\" to an id. Skipping.");
                continue;
            }

            SCColor? blockColor = null;
            int blockData = 0;
            if (match.Groups.Count > 1)
            {
                string obtainedColorName = groups.FirstOrDefault((group) => colorNames.Contains(group.Value))?.Value; // Get the first group that is NOT a number
                if (colorNames.Contains(obtainedColorName))
                    blockColor = Enum.Parse<SCColor>(obtainedColorName, true);
                //else if (!string.IsNullOrEmpty(obtainedColorName))
                //    Console.WriteLine($"Warning: Could not identify the color \"{obtainedColorName}\" in the translation value \"{pair.Value}\". Ignoring.");
                string obtainedBlockData = groups.FirstOrDefault((group) => int.TryParse(group.Value, out _))?.Value; // Get the first group that IS a number.
                if (obtainedBlockData != null)
                    blockData = int.Parse(obtainedBlockData);
            }
            if (blockColor.HasValue)
            {
                if (blockName.Contains("Stairs"))
                    blockData = PaintStair(blockData, blockColor.Value);
                else
                    blockData = Paint(blockData, blockColor.Value);
            }
            BlockTranslator[pair.Key] = Terrain.MakeBlockValue(blockId, 0, blockData);
        }
        Console.ReadKey();
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
        //Console.ReadKey();
    }
    
    private double processDuration_sum = 0.0;
    private double processDuration_count = 0.0;
    public int processingThreadCount = 0;
    public bool validRegionsFound = false;
    
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
                        validRegionsFound = true;
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
    public static int Paint(int data, SCColor color)
    {
        data = (data & -32) | 1 | (((int) color) << 1);
        return data;
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
    
    public static int PaintStair(int data, SCColor color)
	{
		return (data & -993) | 0x20 | (((int) color & 0xF) << 6);
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

    public static Dictionary<string, int> BlockTranslator = new Dictionary<string, int>();
}