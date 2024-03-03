using System;
using Substrate;
using Substrate.Nbt;
using Substrate.Core;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using Game;

static class Program
{
    static void Main (string[] args)
    {
        if (args.Length < 1) {
            Console.WriteLine("Usage: Converter.exe <minecraft world>");
            Console.WriteLine("Or drag the world folder to the executable.");
            Console.ReadKey();
            return;
        }
        
        string src = args[0];
        
        if (!File.Exists(Path.Combine(src, "level.dat")))
        {
            Console.WriteLine("Not a valid Minecraft world. `level.dat` not found.");
            Console.ReadKey();
            return;
        }
        
        bool isJava = Directory.Exists(Path.Combine(src, "region/"));
        
        if (Directory.Exists("tmp_sc"))
            Directory.Delete("tmp_sc/", true);
        Directory.CreateDirectory("tmp_sc");
         
        
        ProjectSerializer sc_project = new ProjectSerializer();
        
        if (isJava)
        {
            JavaLevelConverter converter = new JavaLevelConverter(src, sc_project);
            converter.Convert();
            
            // Convert chunks
            Console.WriteLine("Converting chunks...");
        }
        else
        {
            Console.WriteLine("Bedrock worlds aren't supported yet. Convert using Chunker.");
            Console.ReadKey();
            return;
        }
        sc_project.Save("tmp_sc/Project.xml");
        
        CompressFiles("tmp_sc", "Result.scworld");
        Directory.Delete("tmp_sc/", true);
        Console.WriteLine("Operation completed. Check Result.scworld.");
        Console.ReadKey();
    }
    
    public static void DebugTree(TagNodeCompound root, string start="")
    {
        foreach (KeyValuePair<string, Substrate.Nbt.TagNode> node in root)
        {
            if (node.Value is TagNodeCompound)
            {
                Console.WriteLine(start + node.Key + " (Compound):");
                DebugTree((TagNodeCompound) node.Value, start + "  ");
            }
            else if (node.Value is TagNodeList nodeList)
            {
                Console.WriteLine(start + node.Key + " (List):");
                foreach (TagNode nodeInList in nodeList)
                {
                    if (nodeInList is TagNodeCompound)
                    {
                        Console.WriteLine(start + "  Compound:");
                        DebugTree((TagNodeCompound) nodeInList, start + "    ");
                    }
                    else
                        Console.WriteLine(start + "  " + nodeInList);
                }
            }
            else if (node.Value is TagNodeByteArray bat)
                Console.WriteLine(start + node.Key + " > " + node.Value.GetType().Name.Substring("TagNode".Length) + "[" + bat.Length + "]");
            else if (node.Value is TagNodeIntArray iat)
                Console.WriteLine(start + node.Key + " > " + node.Value.GetType().Name.Substring("TagNode".Length) + "[" + iat.Length + "]");
            else if (node.Value is TagNodeShortArray sat)
                Console.WriteLine(start + node.Key + " > " + node.Value.GetType().Name.Substring("TagNode".Length) + "[" + sat.Length + "]");
            else if (node.Value is TagNodeLongArray lat)
                Console.WriteLine(start + node.Key + " > " + node.Value.GetType().Name.Substring("TagNode".Length) + "[" + lat.Length + "]");
            else
                Console.WriteLine(start + node.Key + " > " + node.Value.GetType().Name.Substring("TagNode".Length) + "("+ node.Value+")");
        }
    }
    
    public static void CompressFiles(string sourceFolderPath, string destinationFilePath)
    {
        string[] files = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);

        using (FileStream destinationFileStream = File.Create(destinationFilePath))
        {
            using (ZipArchive archive = new ZipArchive(destinationFileStream, ZipArchiveMode.Create))
            {
                foreach (string filePath in files)
                {
                    string relativePath = filePath.Substring(sourceFolderPath.Length + 1);

                    ZipArchiveEntry entry = archive.CreateEntryFromFile(filePath, relativePath);
                }
            }
        }
    }
    
    public static string GetText(this TagNodeCompound dtree, string name, string default_value)
    {
        try
        {
            return dtree[name].ToTagString();
        }
        catch
        {
            if (default_value == null)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return default_value;
    }
    
    public static int GetInt(this TagNodeCompound dtree, string name, int default_value)
    {
        try
        {
            return dtree[name].ToTagInt();
        }
        catch
        {
            if (default_value == null)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return default_value;
    }
    
    public static long GetLong(this TagNodeCompound dtree, string name, long default_value)
    {
        try
        {
            return dtree[name].ToTagLong();
        }
        catch
        {
            if (default_value == null)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return default_value;
    }
    
    public static byte GetByte(this TagNodeCompound dtree, string name, byte default_value)
    {
        try
        {
            return dtree[name].ToTagByte();
        }
        catch
        {
            if (default_value == null)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return default_value;
    }
    
    public static float GetFloat(this TagNodeCompound dtree, string name, float default_value)
    {
        try
        {
            return dtree[name].ToTagFloat();
        }
        catch
        {
            if (default_value == null)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return default_value;
    }
    
    public static TagNodeCompound GetCompound(this TagNodeCompound dtree, string name, bool optional)
    {
        try
        {
            return dtree[name].ToTagCompound();
        }
        catch
        {
            if (!optional)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return null;
    }
    
    public static TagNodeList GetList(this TagNodeCompound dtree, string name, bool optional)
    {
        try
        {
            return dtree[name].ToTagList();
        }
        catch
        {
            if (!optional)
            {
                throw new Exception($"Error obtaining the required tag `{name}`");
            }
        }
        return null;
    }
    
    
    
    

    /*public static Dictionary<int,int> BlockTranslator = new Dictionary<int,int>()
    {
        {BlockType.AIR, 0},
        { BlockType.STONE, 3},
        { BlockType.GRASS, 3},
        { BlockType.DIRT, 2},
        { BlockType.COBBLESTONE, 5},
        { BlockType.WOOD_PLANK, 21},
        { BlockType.SAPLING, 0},
        { BlockType.BEDROCK, 1},
        { BlockType.WATER, 18},
        { BlockType.STATIONARY_WATER, 18},
        { BlockType.LAVA, 92},
        { BlockType.STATIONARY_LAVA, 92},
        { BlockType.SAND, 7},
        { BlockType.GRAVEL, 6},
        { BlockType.GOLD_ORE, 0},
        { BlockType.IRON_ORE, 39},
        { BlockType.COAL_ORE, 16},
        { BlockType.WOOD, 9},
        { BlockType.LEAVES, 12},
        { BlockType.SPONGE, 72},
        { BlockType.GLASS, 15},
        { BlockType.LAPIS_ORE, 0},
        { BlockType.LAPIS_BLOCK, 0},
        { BlockType.DISPENSER, 216},
        { BlockType.SANDSTONE, 4},
        { BlockType.NOTE_BLOCK, 183},
        { BlockType.BED, 55},
        { BlockType.POWERED_RAIL, 208},
        { BlockType.DETECTOR_RAIL, 208},
        { BlockType.STICKY_PISTON, 237},
        { BlockType.COBWEB, 0},
        { BlockType.TALL_GRASS, 19},
        { BlockType.DEAD_SHRUB, 0},
        { BlockType.PISTON, 237},
        { BlockType.PISTON_HEAD, 238},
        { BlockType.WOOL, 0},
        { BlockType.PISTON_MOVING, 0},
        { BlockType.YELLOW_FLOWER, 0},
        { BlockType.RED_ROSE, 0},
        { BlockType.BROWN_MUSHROOM, 0},
        { BlockType.RED_MUSHROOM, 0},
        { BlockType.GOLD_BLOCK, 47},
        { BlockType.IRON_BLOCK, 46},
        { BlockType.DOUBLE_STONE_SLAB, 0},
        { BlockType.STONE_SLAB, 53},
        { BlockType.BRICK_BLOCK, 73},
        { BlockType.TNT, 107},
        { BlockType.BOOKSHELF, 0},
        { BlockType.MOSS_STONE, 3},
        { BlockType.OBSIDIAN, 67},
        { BlockType.TORCH, 31},
        { BlockType.FIRE, 104},
        { BlockType.MONSTER_SPAWNER, 0},
        { BlockType.WOOD_STAIRS, 0},
        { BlockType.CHEST, 45},
        { BlockType.REDSTONE_WIRE, 133},
        { BlockType.DIAMOND_ORE, 112},
        { BlockType.DIAMOND_BLOCK, 126},
        { BlockType.CRAFTING_TABLE, 27},
        { BlockType.CROPS, 119},
        { BlockType.FARMLAND, 168},
        { BlockType.FURNACE, 64},
        { BlockType.BURNING_FURNACE, 65},
        { BlockType.SIGN_POST, 210},
        { BlockType.WOOD_DOOR, 0},
        { BlockType.LADDER, 57},
        { BlockType.RAILS, 0},
        { BlockType.COBBLESTONE_STAIRS, 0},
        { BlockType.WALL_SIGN, 211},
        { BlockType.LEVER, 141},
        { BlockType.STONE_PLATE, 144},
        { BlockType.IRON_DOOR, 57},
        { BlockType.WOOD_PLATE, 144},
        { BlockType.REDSTONE_ORE, 0},
        { BlockType.GLOWING_REDSTONE_ORE, 0},
        { BlockType.REDSTONE_TORCH_OFF, 0},
        { BlockType.REDSTONE_TORCH_ON, 0},
        { BlockType.STONE_BUTTON, 0},
        { BlockType.SNOW, 61},
        { BlockType.ICE, 62},
        { BlockType.SNOW_BLOCK, 62}, // Bruh??
        { BlockType.CACTUS, 127},
        { BlockType.CLAY_BLOCK, 0},
        { BlockType.SUGAR_CANE, 0},
        { BlockType.JUKEBOX, 0},
        { BlockType.FENCE, 0},
        { BlockType.PUMPKIN, 0},
        { BlockType.NETHERRACK, 0},
        { BlockType.SOUL_SAND, 0},
        { BlockType.GLOWSTONE_BLOCK, 0},
        { BlockType.PORTAL, 0},
        { BlockType.JACK_O_LANTERN, 132},
        { BlockType.CAKE_BLOCK, 0},
        { BlockType.REDSTONE_REPEATER_OFF, 145},
        { BlockType.REDSTONE_REPEATER_ON, 145},
        { BlockType.LOCKED_CHEST, 21},
        { BlockType.STAINED_GLASS, 0},
        { BlockType.TRAPDOOR, 83},
        { BlockType.SILVERFISH_STONE, 0},
        { BlockType.STONE_BRICK, 0},
        { BlockType.HUGE_RED_MUSHROOM, 0},
        { BlockType.HUGE_BROWN_MUSHROOM, 0},
        { BlockType.IRON_BARS, 40},
        { BlockType.GLASS_PANE, 0},
        { BlockType.MELON, 0},
        { BlockType.PUMPKIN_STEM, 0},
        { BlockType.MELON_STEM, 0},
        { BlockType.VINES, 197},
        { BlockType.FENCE_GATE, 166},
        { BlockType.BRICK_STAIRS, 0},
        { BlockType.STONE_BRICK_STAIRS, 0},
        { BlockType.MYCELIUM, 0},
        { BlockType.LILLY_PAD, 0},
        { BlockType.NETHER_BRICK, 0},
        { BlockType.NETHER_BRICK_FENCE, 0},
        { BlockType.NETHER_BRICK_STAIRS, 0},
        { BlockType.NETHER_WART, 0},
        { BlockType.ENCHANTMENT_TABLE, 0},
        { BlockType.BREWING_STAND, 0},
        { BlockType.CAULDRON, 0},
        { BlockType.END_PORTAL, 0},
        { BlockType.END_PORTAL_FRAME, 4},
        { BlockType.END_STONE, 4},
        { BlockType.DRAGON_EGG, 0},
        { BlockType.REDSTONE_LAMP_OFF, 231},
        { BlockType.REDSTONE_LAMP_ON, 17},
        { BlockType.DOUBLE_WOOD_SLAB, 0},
        { BlockType.WOOD_SLAB, 0},
        { BlockType.COCOA_PLANT, 0},
        { BlockType.SANDSTONE_STAIRS, 0},
        { BlockType.EMERALD_ORE, 0},
        { BlockType.ENDER_CHEST, 0},
        { BlockType.TRIPWIRE_HOOK, 0},
        { BlockType.TRIPWIRE, 0},
        { BlockType.EMERALD_BLOCK, 0},
        { BlockType.SPRUCE_WOOD_STAIRS, 0},
        { BlockType.BIRCH_WOOD_STAIRS, 0},
        { BlockType.JUNGLE_WOOD_STAIRS, 0},
        { BlockType.COMMAND_BLOCK, 186},
        { BlockType.BEACON_BLOCK, 0},
        { BlockType.COBBLESTONE_WALL, 0},
        { BlockType.FLOWER_POT, 0},
        { BlockType.CARROTS, 0},
        { BlockType.POTATOES, 0},
        { BlockType.WOOD_BUTTON, 142},
        { BlockType.HEADS, 0},
        { BlockType.ANVIL, 0},
        { BlockType.TRAPPED_CHEST, 45},
        { BlockType.WEIGHTED_PRESSURE_PLATE_LIGHT, 144},
        { BlockType.WEIGHTED_PRESSURE_PLATE_HEAVY, 144},
        { BlockType.REDSTONE_COMPARATOR_INACTIVE, 186},
        { BlockType.REDSTONE_COMPARATOR_ACTIVE, 186},
        { BlockType.DAYLIGHT_SENSOR, 151},
        { BlockType.REDSTONE_BLOCK, 0},
        { BlockType.NETHER_QUARTZ_ORE, 0},
        { BlockType.HOPPER, 0},
        { BlockType.QUARTZ_BLOCK, 0},
        { BlockType.QUARTZ_STAIRS, 0},
        { BlockType.ACTIVATOR_RAIL, 0},
        { BlockType.DROPPER, 0},
        { BlockType.STAINED_CLAY, 0},
        { BlockType.STAINED_GLASS_PANE, 0},
        { BlockType.HAY_BLOCK, 0},
        { BlockType.CARPET, 208},
        { BlockType.HARDENED_CLAY, 0},
        { BlockType.COAL_BLOCK, 0},
    };
    */
}