using System;
using System.Collections.Generic;
using Engine;

namespace Game
{
	public class TerrainChunk
	{
		public const int SizeBits = 4;

		public const int Size = 16;

		public const int HeightBits = 8;

		public const int Height = 256;

		public const int SizeMinusOne = 15;

		public const int HeightMinusOne = 255;

		public const int SliceHeight = 16;

		public const int SlicesCount = 16;

		public Point2 Coords;

		public Point2 Origin;
		
		public int[] Cells = new int[65536];

		public int[] Shafts = new int[256];

		public TerrainChunk(int x, int z)
		{
			Coords = new Point2(x, z);
			Origin = new Point2(x * 16, z * 16);
			
		}

		public static bool IsCellValid(int x, int y, int z)
		{
			if (x >= 0 && x < 16 && y >= 0 && y < 256 && z >= 0)
			{
				return z < 16;
			}
			return false;
		}

		public static bool IsShaftValid(int x, int z)
		{
			if (x >= 0 && x < 16 && z >= 0)
			{
				return z < 16;
			}
			return false;
		}

		public static int CalculateCellIndex(int x, int y, int z)
		{
			return y + x * 256 + z * 256 * 16;
		}

		public int CalculateTopmostCellHeight(int x, int z)
		{
			int num = CalculateCellIndex(x, 255, z);
			int num2 = 255;
			while (num2 >= 0)
			{
				if (Terrain.ExtractContents(GetCellValueFast(num)) != 0)
				{
					return num2;
				}
				num2--;
				num--;
			}
			return 0;
		}

		public int GetCellValueFast(int index)
		{
			return Cells[index];
		}

		public int GetCellValueFast(int x, int y, int z)
		{
			return Cells[y + x * 256 + z * 256 * 16];
		}

		public void SetCellValueFast(int x, int y, int z, int value)
		{
			Cells[y + x * 256 + z * 256 * 16] = value;
		}

		public void SetCellValueFast(int index, int value)
		{
			Cells[index] = value;
		}

		public int GetCellContentsFast(int x, int y, int z)
		{
			return Terrain.ExtractContents(GetCellValueFast(x, y, z));
		}

		public int GetCellLightFast(int x, int y, int z)
		{
			return Terrain.ExtractLight(GetCellValueFast(x, y, z));
		}

		public int GetShaftValueFast(int x, int z)
		{
			return Shafts[x + z * 16];
		}

		public void SetShaftValueFast(int x, int z, int value)
		{
			Shafts[x + z * 16] = value;
		}
		
		public void Postcalculate()
		{
		    for (int x=0; x < 16; x++)
		    {
		        for (int z=0; z < 16; z++)
		        {
		            int currentHeight = 255;
		            int currentCell = CalculateCellIndex(x, 255, z);
		            bool topmostSet = false;
		            bool isFluid = false;
		            while (currentHeight > 0)
		            {
		                int block = GetCellValueFast(currentCell);
		                int contents = Terrain.ExtractContents(block);
		                if (contents != 0 && !topmostSet)
		                {
		                    SetTopHeightFast(x, z, currentHeight);
		                    SetSunlightHeightFast(x, z, currentHeight);
		                    topmostSet = true;
		                }
		                if (contents == 18 || contents == 92)
		                {
		                    if (!isFluid)
		                    {
		                        isFluid = true;
		                        SetCellValueFast(currentCell, JavaLevelConverter.SetIsTop(block));
		                    }
		                }
		                else isFluid = false;
		                if (JavaLevelConverter.IsFence(block))
		                    UpdateFence(block, x, currentHeight, z);
		                currentHeight--;
		                currentCell--;
		            }
		            
		            SetBottomHeightFast(x, z, 0);
		            SetTemperatureFast(x, z, 12);
		            SetHumidityFast(x, z, 12);
		        }
		    }
		}
		
		public void UpdateFence(int value, int x, int y, int z)
		{
			int variant = 0;
            if (IsCellValid(x + 1, y, z) && JavaLevelConverter.IsFence(GetCellValueFast(x + 1, y, z)))
                variant++;
            if (IsCellValid(x - 1, y, z) && JavaLevelConverter.IsFence(GetCellValueFast(x - 1, y, z)))
                variant += 2;
            if (IsCellValid(x, y, z + 1) && JavaLevelConverter.IsFence(GetCellValueFast(x, y, z+1)))
                variant += 4;
            if (IsCellValid(x, y, z - 1) && JavaLevelConverter.IsFence(GetCellValueFast(x, y, z-1)))
                variant += 8;
		    
			SetCellValueFast(x, y, z, JavaLevelConverter.SetVariant(value, variant));
		}

		public int GetTemperatureFast(int x, int z)
		{
			return Terrain.ExtractTemperature(GetShaftValueFast(x, z));
		}

		public void SetTemperatureFast(int x, int z, int temperature)
		{
			SetShaftValueFast(x, z, Terrain.ReplaceTemperature(GetShaftValueFast(x, z), temperature));
		}

		public int GetHumidityFast(int x, int z)
		{
			return Terrain.ExtractHumidity(GetShaftValueFast(x, z));
		}

		public void SetHumidityFast(int x, int z, int humidity)
		{
			SetShaftValueFast(x, z, Terrain.ReplaceHumidity(GetShaftValueFast(x, z), humidity));
		}

		public int GetTopHeightFast(int x, int z)
		{
			return Terrain.ExtractTopHeight(GetShaftValueFast(x, z));
		}

		public void SetTopHeightFast(int x, int z, int topHeight)
		{
			SetShaftValueFast(x, z, Terrain.ReplaceTopHeight(GetShaftValueFast(x, z), topHeight));
		}

		public int GetBottomHeightFast(int x, int z)
		{
			return Terrain.ExtractBottomHeight(GetShaftValueFast(x, z));
		}

		public void SetBottomHeightFast(int x, int z, int bottomHeight)
		{
			SetShaftValueFast(x, z, Terrain.ReplaceBottomHeight(GetShaftValueFast(x, z), bottomHeight));
		}

		public int GetSunlightHeightFast(int x, int z)
		{
			return Terrain.ExtractSunlightHeight(GetShaftValueFast(x, z));
		}

		public void SetSunlightHeightFast(int x, int z, int sunlightHeight)
		{
			SetShaftValueFast(x, z, Terrain.ReplaceSunlightHeight(GetShaftValueFast(x, z), sunlightHeight));
		}
	}
}
