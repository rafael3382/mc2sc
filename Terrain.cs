using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Engine;

namespace Game
{
	public class Terrain
	{
		public Dictionary<Point2,TerrainChunk> AllocatedChunks = new Dictionary<Point2,TerrainChunk>();

		public void Dispose()
		{
			AllocatedChunks.Clear();
		}

		public TerrainChunk GetChunkAtCoords(int chunkX, int chunkZ)
		{
		    if (!AllocatedChunks.ContainsKey(new Point2(chunkX, chunkZ)))
		        return null;
			return AllocatedChunks[new Point2(chunkX, chunkZ)];
		}

		public TerrainChunk GetChunkAtCell(int x, int z)
		{
			return GetChunkAtCoords(x >> 4, z >> 4);
		}

		public TerrainChunk AllocateChunk(int chunkX, int chunkZ)
		{
			if (GetChunkAtCoords(chunkX, chunkZ) != null)
			{
				throw new InvalidOperationException("Chunk already allocated.");
			}
			TerrainChunk terrainChunk = new TerrainChunk(chunkX, chunkZ);
			AllocatedChunks.Add(new Point2(chunkX, chunkZ), terrainChunk);
			return terrainChunk;
		}

		public void FreeChunk(TerrainChunk chunk)
		{
			if (!AllocatedChunks.Remove(chunk.Coords))
			{
				throw new InvalidOperationException("Chunk not allocated.");
			}
		}

		public static int ComparePoints(Point2 c1, Point2 c2)
		{
			if (c1.Y == c2.Y)
			{
				return c1.X - c2.X;
			}
			return c1.Y - c2.Y;
		}

		public static Point2 ToChunk(Vector2 p)
		{
			return ToChunk(ToCell(p.X), ToCell(p.Y));
		}

		public static Point2 ToChunk(int x, int z)
		{
			return new Point2(x >> 4, z >> 4);
		}

		public static int ToCell(float x)
		{
			return (int)MathUtils.Floor(x);
		}

		public static Point2 ToCell(float x, float y)
		{
			return new Point2((int)MathUtils.Floor(x), (int)MathUtils.Floor(y));
		}

		public static Point2 ToCell(Vector2 p)
		{
			return new Point2((int)MathUtils.Floor(p.X), (int)MathUtils.Floor(p.Y));
		}

		public static Point3 ToCell(float x, float y, float z)
		{
			return new Point3((int)MathUtils.Floor(x), (int)MathUtils.Floor(y), (int)MathUtils.Floor(z));
		}

		public static Point3 ToCell(Vector3 p)
		{
			return new Point3((int)MathUtils.Floor(p.X), (int)MathUtils.Floor(p.Y), (int)MathUtils.Floor(p.Z));
		}

		public bool IsCellValid(int x, int y, int z)
		{
			if (y >= 0)
			{
				return y < 256;
			}
			return false;
		}

		public int GetCellValue(int x, int y, int z)
		{
			if (!IsCellValid(x, y, z))
			{
				return 0;
			}
			return GetCellValueFast(x, y, z);
		}

		public int GetCellContents(int x, int y, int z)
		{
			if (!IsCellValid(x, y, z))
			{
				return 0;
			}
			return GetCellContentsFast(x, y, z);
		}

		public int GetCellLight(int x, int y, int z)
		{
			if (!IsCellValid(x, y, z))
			{
				return 0;
			}
			return GetCellLightFast(x, y, z);
		}

		public int GetCellValueFast(int x, int y, int z)
		{
			return GetChunkAtCell(x, z)?.GetCellValueFast(x & 0xF, y, z & 0xF) ?? 0;
		}

		public int GetCellValueFastChunkExists(int x, int y, int z)
		{
			return GetChunkAtCell(x, z).GetCellValueFast(x & 0xF, y, z & 0xF);
		}

		public int GetCellContentsFast(int x, int y, int z)
		{
			return ExtractContents(GetCellValueFast(x, y, z));
		}

		public int GetCellLightFast(int x, int y, int z)
		{
			return ExtractLight(GetCellValueFast(x, y, z));
		}

		public void SetCellValueFast(int x, int y, int z, int value)
		{
			GetChunkAtCell(x, z)?.SetCellValueFast(x & 0xF, y, z & 0xF, value);
		}

		public int CalculateTopmostCellHeight(int x, int z)
		{
			return GetChunkAtCell(x, z)?.CalculateTopmostCellHeight(x & 0xF, z & 0xF) ?? 0;
		}

		public int GetShaftValue(int x, int z)
		{
			return GetChunkAtCell(x, z)?.GetShaftValueFast(x & 0xF, z & 0xF) ?? 0;
		}

		public void SetShaftValue(int x, int z, int value)
		{
			GetChunkAtCell(x, z)?.SetShaftValueFast(x & 0xF, z & 0xF, value);
		}

		public int GetTemperature(int x, int z)
		{
			return ExtractTemperature(GetShaftValue(x, z));
		}

		public void SetTemperature(int x, int z, int temperature)
		{
			SetShaftValue(x, z, ReplaceTemperature(GetShaftValue(x, z), temperature));
		}

		public int GetHumidity(int x, int z)
		{
			return ExtractHumidity(GetShaftValue(x, z));
		}

		public void SetHumidity(int x, int z, int humidity)
		{
			SetShaftValue(x, z, ReplaceHumidity(GetShaftValue(x, z), humidity));
		}

		public int GetTopHeight(int x, int z)
		{
			return ExtractTopHeight(GetShaftValue(x, z));
		}

		public void SetTopHeight(int x, int z, int topHeight)
		{
			SetShaftValue(x, z, ReplaceTopHeight(GetShaftValue(x, z), topHeight));
		}

		public int GetBottomHeight(int x, int z)
		{
			return ExtractBottomHeight(GetShaftValue(x, z));
		}

		public void SetBottomHeight(int x, int z, int bottomHeight)
		{
			SetShaftValue(x, z, ReplaceBottomHeight(GetShaftValue(x, z), bottomHeight));
		}

		public int GetSunlightHeight(int x, int z)
		{
			return ExtractSunlightHeight(GetShaftValue(x, z));
		}

		public void SetSunlightHeight(int x, int z, int sunlightHeight)
		{
			SetShaftValue(x, z, ReplaceSunlightHeight(GetShaftValue(x, z), sunlightHeight));
		}

		public static int MakeBlockValue(int contents)
		{
			return contents & 0x3FF;
		}

		public static int MakeBlockValue(int contents, int light, int data)
		{
			return (contents & 0x3FF) | ((light << 10) & 0x3C00) | ((data << 14) & -16384);
		}

		public static int ExtractContents(int value)
		{
			return value & 0x3FF;
		}

		public static int ExtractLight(int value)
		{
			return (value & 0x3C00) >> 10;
		}

		public static int ExtractData(int value)
		{
			return (value & -16384) >> 14;
		}

		public static int ExtractTopHeight(int value)
		{
			return value & 0xFF;
		}

		public static int ExtractBottomHeight(int value)
		{
			return (value & 0xFF0000) >> 16;
		}

		public static int ExtractSunlightHeight(int value)
		{
			return (value & -16777216) >> 24;
		}

		public static int ExtractHumidity(int value)
		{
			return (value & 0xF000) >> 12;
		}

		public static int ExtractTemperature(int value)
		{
			return (value & 0xF00) >> 8;
		}

		public static int ReplaceContents(int value, int contents)
		{
			return value ^ ((value ^ contents) & 0x3FF);
		}

		public static int ReplaceLight(int value, int light)
		{
			return value ^ ((value ^ (light << 10)) & 0x3C00);
		}

		public static int ReplaceData(int value, int data)
		{
			return value ^ ((value ^ (data << 14)) & -16384);
		}

		public static int ReplaceTopHeight(int value, int topHeight)
		{
			return value ^ ((value ^ topHeight) & 0xFF);
		}

		public static int ReplaceBottomHeight(int value, int bottomHeight)
		{
			return value ^ ((value ^ (bottomHeight << 16)) & 0xFF0000);
		}

		public static int ReplaceSunlightHeight(int value, int sunlightHeight)
		{
			return value ^ ((value ^ (sunlightHeight << 24)) & -16777216);
		}

		public static int ReplaceHumidity(int value, int humidity)
		{
			return value ^ ((value ^ (humidity << 12)) & 0xF000);
		}

		public static int ReplaceTemperature(int value, int temperature)
		{
			return value ^ ((value ^ (temperature << 8)) & 0xF00);
		}
	}
}
