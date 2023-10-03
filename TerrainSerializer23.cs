using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Engine;

namespace Game
{
	public class TerrainSerializer23 : IDisposable
	{
		private interface IStorage : IDisposable
		{
			void Open(string directoryName, string suffix);

			int Load(Point2 coords, byte[] buffer);

			void Save(Point2 coords, byte[] buffer, int size);
		}

		private class SingleFileStorage : IStorage, IDisposable
		{
			private struct ChunkDescriptor
			{
				public int Index;

				public Point2 Coords;

				public int StartNode;
			}

			private const string FileName = "Chunks32fs.dat";

			private const uint FileHeaderMagic = 3735923200u;

			private const int FileHeaderSize = 786444;

			private const int FileHeaderFreeNodeOffset = 8;

			private const int FileHeaderChunkDescriptorsOffset = 12;

			private const int FileHeaderChunkDescriptorsCount = 65536;

			private const int FileHeaderChunkDescriptorSize = 12;

			private const uint NodeHeaderMagic = 3735927296u;

			private const int NodeHeaderSize = 8;

			private Stream Stream;

			private BinaryReader Reader;

			private BinaryWriter Writer;

			private Dictionary<Point2, ChunkDescriptor> ChunkDescriptors = new Dictionary<Point2, ChunkDescriptor>();

			private int FreeNode;

			private int NodeSize;

			private int NodeDataSize => NodeSize - 8;

			public void Open(string directoryName, string suffix)
			{
				string path = Path.Combine(directoryName, "Chunks32fs.dat" + suffix);
				try
				{
					Stream = File.Open(path, FileMode.OpenOrCreate);
					Reader = new BinaryReader(Stream);
					Writer = new BinaryWriter(Stream);
					if (Stream.Length == 0L)
					{
						FreeNode = -1;
						NodeSize = 1024;
						Writer.Write(ReverseEndianness(3735923200u));
						Writer.Write(NodeSize);
						Writer.Write(FreeNode);
						for (int i = 0; i < 65536; i++)
						{
							WriteChunkDescriptor(new ChunkDescriptor
							{
								Index = i,
								StartNode = -1
							});
						}
					}
				}
				catch (Exception ex)
				{
					File.Delete(path);
					throw ex;
				}
				Stream.Position = 0L;
				if (ReverseEndianness(Reader.ReadUInt32()) != 3735923200u)
				{
					throw new InvalidOperationException("Invalid chunks file header magic.");
				}
				NodeSize = Reader.ReadInt32();
				if (NodeSize < 64 || NodeSize > 65536)
				{
					throw new InvalidOperationException("Invalid chunks file header node size.");
				}
				FreeNode = Reader.ReadInt32();
				for (int j = 0; j < 65536; j++)
				{
					ChunkDescriptor value = ReadChunkDescriptor(j);
					if (value.StartNode >= 0)
					{
						ChunkDescriptors.Add(value.Coords, value);
					}
				}
			}

			public void Dispose()
			{
				if (Stream != null)
				{
					Stream.Dispose();
				}
			}

			public int Load(Point2 p, byte[] buffer)
			{
				if (!ChunkDescriptors.TryGetValue(p, out var value))
				{
					return -1;
				}
				int nextNode = value.StartNode;
				int num = 0;
				while (nextNode >= 0)
				{
					num += ReadNode(nextNode, buffer, num, out nextNode);
				}
				return num;
			}

			public void Save(Point2 p, byte[] buffer, int size)
			{
				int count = Math.Max((size + NodeDataSize - 1) / NodeDataSize, 1);
				List<int> freeNodes = GetFreeNodes(count);
				ReadNode(freeNodes.Last(), null, 0, out var nextNode);
				int num = 0;
				for (int i = 0; i < freeNodes.Count; i++)
				{
					int num2 = Math.Min(size - num, NodeDataSize);
					WriteNode(freeNodes[i], buffer, num, num2, (i < freeNodes.Count - 1) ? freeNodes[i + 1] : (-1));
					num += num2;
				}
				if (!ChunkDescriptors.TryGetValue(p, out var value))
				{
					ChunkDescriptor chunkDescriptor = default(ChunkDescriptor);
					chunkDescriptor.Index = ChunkDescriptors.Count % 65536;
					chunkDescriptor.Coords = p;
					chunkDescriptor.StartNode = freeNodes.First();
					value = chunkDescriptor;
					SetAndWriteFreeNode(nextNode);
				}
				else
				{
					int node = FindLastNode(value.StartNode);
					WriteNode(node, null, 0, 0, nextNode);
					SetAndWriteFreeNode(value.StartNode);
					value.StartNode = freeNodes.First();
				}
				WriteChunkDescriptor(value);
				ChunkDescriptors[p] = value;
			}

			private List<int> GetFreeNodes(int count)
			{
				List<int> list = new List<int>();
				int nextNode = FreeNode;
				while (nextNode >= 0 && list.Count < count)
				{
					list.Add(nextNode);
					ReadNode(nextNode, null, 0, out nextNode);
				}
				if (list.Count < count)
				{
					int num = count - list.Count;
					int num2 = (int)((Stream.Length - 786444) / NodeSize);
					int num3 = num2 + num - 1;
					Stream.SetLength(Stream.Length + NodeSize * num);
					WriteNode(num3, null, 0, 0, -1);
					if (list.Count > 0)
					{
						WriteNode(list.Last(), null, 0, 0, num2);
					}
					else
					{
						SetAndWriteFreeNode(num2);
					}
					for (int i = num2; i <= num3; i++)
					{
						list.Add(i);
					}
				}
				return list;
			}

			private int FindLastNode(int startNode)
			{
				int num = startNode;
				while (true)
				{
					ReadNode(num, null, 0, out var nextNode);
					if (nextNode < 0)
					{
						break;
					}
					num = nextNode;
				}
				return num;
			}

			private void SetAndWriteFreeNode(int freeNode)
			{
				Stream.Position = 8L;
				Writer.Write(freeNode);
				FreeNode = freeNode;
			}

			private ChunkDescriptor ReadChunkDescriptor(int i)
			{
				Stream.Position = 12 + i * 12;
				ChunkDescriptor result = default(ChunkDescriptor);
				result.Index = i;
				result.Coords.X = Reader.ReadInt32();
				result.Coords.Y = Reader.ReadInt32();
				result.StartNode = Reader.ReadInt32();
				return result;
			}

			private void WriteChunkDescriptor(ChunkDescriptor desc)
			{
				Stream.Position = 12 + desc.Index * 12;
				Writer.Write(desc.Coords.X);
				Writer.Write(desc.Coords.Y);
				Writer.Write(desc.StartNode);
			}

			private int ReadNode(int node, byte[] data, int offset, out int nextNode)
			{
				if (node < 0 || node >= (Stream.Length - 786444) / NodeSize)
				{
					throw new InvalidOperationException("Invalid node.");
				}
				Stream.Position = 786444 + node * NodeSize;
				if (ReverseEndianness(Reader.ReadUInt32()) != 3735927296u)
				{
					throw new InvalidOperationException("Invalid node magic.");
				}
				int nodeHeader = Reader.ReadInt32();
				ParseNodeHeader(node, nodeHeader, out var dataSize, out nextNode);
				if (data != null && Stream.Read(data, offset, dataSize) != dataSize)
				{
					throw new InvalidOperationException("Truncated ChunksFile.");
				}
				return dataSize;
			}

			private void WriteNode(int node, byte[] data, int offset, int size, int nextNode)
			{
				if (node < 0 || node >= (Stream.Length - 786444) / NodeSize)
				{
					throw new InvalidOperationException("Invalid node.");
				}
				Stream.Position = 786444 + node * NodeSize;
				int value = MakeNodeHeader(node, size, nextNode);
				Writer.Write(ReverseEndianness(3735927296u));
				Writer.Write(value);
				if (data != null)
				{
					Stream.Write(data, offset, size);
				}
			}

			private int MakeNodeHeader(int node, int dataSize, int nextNode)
			{
				if (nextNode < 0)
				{
					return (dataSize << 1) | 1;
				}
				return nextNode - (node + 1) << 1;
			}

			private void ParseNodeHeader(int node, int nodeHeader, out int dataSize, out int nextNode)
			{
				if (((uint)nodeHeader & (true ? 1u : 0u)) != 0)
				{
					dataSize = nodeHeader >> 1;
					nextNode = -1;
				}
				else
				{
					dataSize = NodeDataSize;
					nextNode = node + 1 + (nodeHeader >> 1);
				}
			}

			private static uint ReverseEndianness(uint n)
			{
				return ((n & 0xFF000000u) >> 24) | ((n & 0xFF0000) >> 8) | ((n & 0xFF00) << 8) | (n << 24);
			}

			public void LogDebugInfo()
			{
			}
		}

		private class RegionFileStorage : IStorage, IDisposable
		{
			private struct DirectoryEntry
			{
				public int Offset;

				public int Size;
			}

			private const int MaxOpenedStreams = 100;

			private const int ExtraSpaceBytes = 1024;

			private const int RegionChunksBits = 4;

			private const int RegionChunksCount = 16;

			private const int RegionDirectoryOffset = 4;

			private const int RegionDirectoryEntrySize = 8;

			private const int RegionChunksCountMinusOne = 15;

			private const int RegionDataOffset = 2052;

			private const int RegionChunkDataOffset = 4;

			private static uint RegionMagic = MakeFourCC("RGN1");

			private static uint RegionChunkMagic = MakeFourCC("CHK1");

			private string RegionsDirectoryName;

			private string TmpFilePath;

			private Dictionary<Point2, Stream> StreamsByRegion = new Dictionary<Point2, Stream>();

			private Queue<Stream> OpenedStreams = new Queue<Stream>();

			public void Dispose()
			{
				while (OpenedStreams.Count > 0)
				{
					OpenedStreams.Dequeue().Dispose();
				}
			}

			public void Open(string directoryName, string suffix)
			{
				RegionsDirectoryName = Path.Combine(directoryName, "Regions" + suffix);
				Directory.CreateDirectory(RegionsDirectoryName);
				TmpFilePath = Path.Combine(RegionsDirectoryName, "tmp");
				try { File.Delete(TmpFilePath); } catch {}
				foreach (string item in Directory.GetFiles(RegionsDirectoryName))
				{
					if (Path.GetExtension(item) == ".new")
					{
						string text = Path.Combine(RegionsDirectoryName, item);
						string text2 = Path.ChangeExtension(text, "");
						if (!File.Exists(text2))
						{
							File.Move(text, text2);
						}
						else
						{
							File.Delete(text);
						}
					}
				}
			}

			public int Load(Point2 coords, byte[] buffer)
			{
				Point2 region = new Point2(coords.X >> 4, coords.Y >> 4);
				Point2 chunk = new Point2(coords.X & 0xF, coords.Y & 0xF);
				Stream regionStream = GetRegionStream(region, createNew: false);
				if (regionStream != null)
				{
					using (BinaryReader reader = new BinaryReader(regionStream, Encoding.UTF8, leaveOpen: true))
					{
						DirectoryEntry directoryEntry = ReadDirectoryEntry(reader, chunk);
						if (directoryEntry.Offset > 0)
						{
							ReadData(reader, directoryEntry.Offset, buffer, directoryEntry.Size);
							return directoryEntry.Size;
						}
					}
				}
				return -1;
			}

			public void Save(Point2 coords, byte[] buffer, int size)
			{
				Point2 region = new Point2(coords.X >> 4, coords.Y >> 4);
				Point2 point = new Point2(coords.X & 0xF, coords.Y & 0xF);
				Stream regionStream = GetRegionStream(region, createNew: true);
				string text = null;
				using (BinaryReader reader = new BinaryReader(regionStream, Encoding.UTF8, leaveOpen: true))
				{
					using (BinaryWriter writer = new BinaryWriter(regionStream, Encoding.UTF8, leaveOpen: true))
					{
						int num = point.X + 16 * point.Y;
						DirectoryEntry[] array = ReadDirectoryEntries(reader);
						DirectoryEntry directoryEntry = array[num];
						if (directoryEntry.Offset > 0)
						{
							int num2 = FindNextEntryIndex(array, num);
							if (num2 >= 0)
							{
								int num3 = array[num2].Offset - directoryEntry.Offset - 4;
								if (size <= num3)
								{
									WriteData(writer, directoryEntry.Offset, buffer, size);
									Point2 chunk = point;
									DirectoryEntry directoryEntry2 = default(DirectoryEntry);
									directoryEntry2.Offset = directoryEntry.Offset;
									directoryEntry2.Size = size;
									DirectoryEntry entry = directoryEntry2;
									WriteDirectoryEntry(writer, chunk, entry);
									regionStream.Flush();
								}
								else
								{
									text = GetRegionPath(region);
									using (Stream stream = File.Open(TmpFilePath, FileMode.Create))
									{
										using (BinaryWriter binaryWriter = new BinaryWriter(stream))
										{
											DirectoryEntry[] array2 = new DirectoryEntry[array.Length];
											int num4 = 2052;
											for (int i = 0; i < array.Length; i++)
											{
												if (i == num)
												{
													array2[i].Offset = num4;
													array2[i].Size = size;
													num4 += CalculateIdealEntrySpace(array2[i].Size);
												}
												else if (array[i].Offset > 0)
												{
													array2[i].Offset = num4;
													array2[i].Size = array[i].Size;
													num4 += CalculateIdealEntrySpace(array2[i].Size);
												}
											}
											ResizeStream(stream, num4);
											binaryWriter.Write(RegionMagic);
											WriteDirectoryEntries(binaryWriter, array2);
											byte[] buffer2 = new byte[array.Max((DirectoryEntry e) => e.Size)];
											for (int j = 0; j < array.Length; j++)
											{
												if (j == num)
												{
													WriteData(binaryWriter, array2[j].Offset, buffer, size);
												}
												else if (array[j].Offset > 0)
												{
													ReadData(reader, array[j].Offset, buffer2, array[j].Size);
													WriteData(binaryWriter, array2[j].Offset, buffer2, array2[j].Size);
												}
											}
										}
									}
								}
							}
							else
							{
								if (directoryEntry.Offset + 4 + size > regionStream.Length)
								{
									ResizeStream(regionStream, directoryEntry.Offset + CalculateIdealEntrySpace(size));
								}
								WriteData(writer, directoryEntry.Offset, buffer, size);
								Point2 chunk2 = point;
								DirectoryEntry directoryEntry2 = default(DirectoryEntry);
								directoryEntry2.Offset = directoryEntry.Offset;
								directoryEntry2.Size = size;
								DirectoryEntry entry = directoryEntry2;
								WriteDirectoryEntry(writer, chunk2, entry);
								regionStream.Flush();
							}
						}
						else
						{
							int num5 = (int)regionStream.Length;
							ResizeStream(regionStream, num5 + CalculateIdealEntrySpace(size));
							WriteData(writer, num5, buffer, size);
							Point2 chunk3 = point;
							DirectoryEntry directoryEntry2 = default(DirectoryEntry);
							directoryEntry2.Offset = num5;
							directoryEntry2.Size = size;
							DirectoryEntry entry = directoryEntry2;
							WriteDirectoryEntry(writer, chunk3, entry);
							regionStream.Flush();
						}
					}
				}
				if (text != null)
				{
					regionStream.Dispose();
					string text2 = text + ".new";
					File.Move(TmpFilePath, text2);
					File.Move(text2, text);
				}
			}

			private string GetRegionPath(Point2 region)
			{
				return string.Format("{0}/Region {1},{2}.dat", new object[3] { RegionsDirectoryName, region.X, region.Y });
			}

			private Stream GetRegionStream(Point2 region, bool createNew)
			{
				if (!StreamsByRegion.TryGetValue(region, out var value) || value == null || !value.CanRead)
				{
					string regionPath = GetRegionPath(region);
					if (File.Exists(regionPath))
					{
						value = File.Open(regionPath, FileMode.Open);
						using (BinaryReader binaryReader = new BinaryReader(value, Encoding.UTF8, leaveOpen: true))
						{
							if (binaryReader.ReadUInt32() != RegionMagic)
							{
								throw new InvalidOperationException($"Invalid region file {region} magic.");
							}
						}
						OpenedStreams.Enqueue(value);
					}
					else if (createNew)
					{
						value = File.Open(regionPath, FileMode.Create);
						OpenedStreams.Enqueue(value);
						using (BinaryWriter binaryWriter = new BinaryWriter(value, Encoding.UTF8, leaveOpen: true))
						{
							binaryWriter.Write(RegionMagic);
							WriteDirectoryEntries(binaryWriter, new DirectoryEntry[256]);
						}
					}
					else
					{
						value = null;
					}
					StreamsByRegion[region] = value;
					while (OpenedStreams.Count > 100)
					{
						OpenedStreams.Dequeue().Dispose();
					}
				}
				return value;
			}

			private static void ReadData(BinaryReader reader, int offset, byte[] buffer, int size)
			{
				reader.BaseStream.Position = offset;
				if (reader.ReadUInt32() != RegionChunkMagic)
				{
					throw new InvalidOperationException("Invalid region file chunk magic.");
				}
				if (reader.BaseStream.Read(buffer, 0, size) != size)
				{
					throw new InvalidOperationException("Region file is truncated.");
				}
			}

			private static DirectoryEntry ReadDirectoryEntry(BinaryReader reader)
			{
				DirectoryEntry result = default(DirectoryEntry);
				result.Offset = reader.ReadInt32();
				result.Size = reader.ReadInt32();
				if (result.Size < 0 || result.Size > 1048576)
				{
					throw new InvalidOperationException("Region file entry size out of bounds, likely corrupt region file.");
				}
				return result;
			}

			private static DirectoryEntry ReadDirectoryEntry(BinaryReader reader, Point2 chunk)
			{
				int num = chunk.X + 16 * chunk.Y;
				reader.BaseStream.Position = 4 + num * 8;
				return ReadDirectoryEntry(reader);
			}

			private static DirectoryEntry[] ReadDirectoryEntries(BinaryReader reader)
			{
				reader.BaseStream.Position = 4L;
				DirectoryEntry[] array = new DirectoryEntry[256];
				for (int i = 0; i < 256; i++)
				{
					array[i] = ReadDirectoryEntry(reader);
				}
				return array;
			}

			private static void WriteData(BinaryWriter writer, int offset, byte[] buffer, int size)
			{
				writer.BaseStream.Position = offset;
				writer.Write(RegionChunkMagic);
				writer.BaseStream.Write(buffer, 0, size);
			}

			private static void WriteDirectoryEntry(BinaryWriter writer, DirectoryEntry entry)
			{
				writer.Write(entry.Offset);
				writer.Write(entry.Size);
			}

			private static void WriteDirectoryEntry(BinaryWriter writer, Point2 chunk, DirectoryEntry entry)
			{
				int num = chunk.X + 16 * chunk.Y;
				writer.BaseStream.Position = 4 + num * 8;
				WriteDirectoryEntry(writer, entry);
			}

			private static void WriteDirectoryEntries(BinaryWriter writer, DirectoryEntry[] entries)
			{
				writer.BaseStream.Position = 4L;
				for (int i = 0; i < 256; i++)
				{
					WriteDirectoryEntry(writer, entries[i]);
				}
			}

			private static void ResizeStream(Stream stream, int size)
			{
				if (size > 268435456)
				{
					throw new InvalidOperationException("Region file too large.");
				}
				stream.SetLength(size);
			}

			private static int FindNextEntryIndex(DirectoryEntry[] entries, int index)
			{
				int result = -1;
				int num = int.MaxValue;
				for (int i = 0; i < entries.Length; i++)
				{
					int num2 = entries[i].Offset - entries[index].Offset;
					if (num2 > 0 && num2 < num)
					{
						num = num2;
						result = i;
					}
				}
				return result;
			}

			private static int CalculateIdealEntrySpace(int size)
			{
				return size + 1024 + 4;
			}

			private static uint MakeFourCC(string s)
			{
				return ((uint)s[3] << 24) | ((uint)s[2] << 16) | ((uint)s[1] << 8) | s[0];
			}
		}

		private const int ChunkSizeX = 16;

		private const int ChunkSizeY = 256;

		private const int ChunkSizeZ = 16;

		private const int WorstCaseChunkDataSize = 262400;

		private object m_lock = new object();

		private IStorage m_storage;

		private byte[] m_storageBuffer = new byte[262400];

		private byte[] m_compressBuffer = new byte[262400];

		public TerrainSerializer23(string directoryName, string suffix = "")
		{
			m_storage = new RegionFileStorage();
			m_storage.Open(directoryName, suffix);
		}

		public bool LoadChunk(TerrainChunk chunk)
		{
			return LoadChunkData(chunk);
		}

		public void SaveChunk(TerrainChunk chunk)
		{
			SaveChunkData(chunk);
		}

		public bool LoadChunkData(TerrainChunk chunk)
		{
			lock (m_lock)
			{
				try
				{
					int num = m_storage.Load(chunk.Coords, m_storageBuffer);
					if (num < 0)
					{
						return false;
					}
					DecompressChunkData(chunk, m_storageBuffer, num);
				}
				catch (Exception e)
				{
					Console.WriteLine(string.Format("Error loading chunk ({0},{1}).", new object[2]
					{
						chunk.Coords.X,
						chunk.Coords.Y
					}));
				    Console.WriteLine(e);
				}
				return true;
			}
		}

		public void SaveChunkData(TerrainChunk chunk)
		{
			lock (m_lock)
			{
				try
				{
					int size = CompressChunkData(chunk, m_storageBuffer);
					m_storage.Save(chunk.Coords, m_storageBuffer, size);
				}
				catch (Exception e)
				{
					Console.WriteLine(string.Format("Error saving chunk ({0},{1}).", new object[2]
					{
						chunk.Coords.X,
						chunk.Coords.Y
					}));
				    Console.WriteLine(e);
				}
			}
		}

		public void Dispose()
		{
			m_storage.Dispose();
		}

		private int CompressChunkData(TerrainChunk chunk, byte[] buffer)
		{
			int num = 0;
			for (int i = 0; i < 16; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					int shaftValueFast = chunk.GetShaftValueFast(i, j);
					m_compressBuffer[num++] = (byte)((Terrain.ExtractTemperature(shaftValueFast) << 4) | Terrain.ExtractHumidity(shaftValueFast));
				}
			}
			int num2 = 0;
			int num3 = -1;
			for (int k = 0; k < 256; k++)
			{
				for (int l = 0; l < 16; l++)
				{
					for (int m = 0; m < 16; m++)
					{
						int num4 = Terrain.ReplaceLight(chunk.GetCellValueFast(m, k, l), 0);
						if (num2 == 0)
						{
							num3 = num4;
							num2 = 1;
							continue;
						}
						if (num4 != num3)
						{
							num = WriteRleValueToBuffer(m_compressBuffer, num, num3, num2);
							num3 = num4;
							num2 = 1;
							continue;
						}
						num2++;
						if (num2 == 271)
						{
							num = WriteRleValueToBuffer(m_compressBuffer, num, num3, num2);
							num2 = 0;
						}
					}
				}
			}
			if (num2 > 0)
			{
				num = WriteRleValueToBuffer(m_compressBuffer, num, num3, num2);
			}
			using (MemoryStream memoryStream = new MemoryStream(buffer))
			{
				using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
				{
					deflateStream.Write(m_compressBuffer, 0, num);
				}
				return (int)memoryStream.Position;
			}
		}

		private void DecompressChunkData(TerrainChunk chunk, byte[] buffer, int size)
		{
			using (DeflateStream deflateStream = new DeflateStream(new MemoryStream(buffer, 0, size), CompressionMode.Decompress))
			{
				size = deflateStream.Read(m_compressBuffer, 0, m_compressBuffer.Length);
				if (size == m_compressBuffer.Length)
				{
					throw new InvalidOperationException("Deflate buffer overflow.");
				}
			}
			int num = 0;
			for (int i = 0; i < 16; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					byte b = m_compressBuffer[num++];
					int value = Terrain.ReplaceTemperature(Terrain.ReplaceHumidity(0, b & 0xF), b >> 4);
					chunk.SetShaftValueFast(i, j, value);
				}
			}
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			while (num < size)
			{
				num = ReadRleValueFromBuffer(m_compressBuffer, num, out var value2, out var count);
				for (int k = 0; k < count; k++)
				{
					chunk.SetCellValueFast(num2, num3, num4, value2);
					num2++;
					if (num2 >= 16)
					{
						num2 = 0;
						num4++;
						if (num4 >= 16)
						{
							num4 = 0;
							num3++;
						}
					}
				}
			}
			if (num2 != 0 || num3 != 256 || num4 != 0)
			{
				throw new InvalidOperationException("Corrupt chunk data.");
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int ReadIntFromBuffer(byte[] buffer, int i)
		{
			return buffer[i] + (buffer[i + 1] << 8) + (buffer[i + 2] << 16) + (buffer[i + 3] << 24);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int ReadRleValueFromBuffer(byte[] buffer, int i, out int value, out int count)
		{
			int value2 = ReadIntFromBuffer(buffer, i);
			int num = Terrain.ExtractLight(value2);
			value = Terrain.ReplaceLight(value2, 0);
			if (num < 15)
			{
				count = num + 1;
				return i + 4;
			}
			count = buffer[i + 4] + 16;
			return i + 5;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void WriteIntToBuffer(byte[] buffer, int i, int data)
		{
			buffer[i] = (byte)data;
			buffer[i + 1] = (byte)(data >> 8);
			buffer[i + 2] = (byte)(data >> 16);
			buffer[i + 3] = (byte)(data >> 24);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int WriteRleValueToBuffer(byte[] buffer, int i, int value, int count)
		{
			if (count < 16)
			{
				int data = Terrain.ReplaceLight(value, count - 1);
				WriteIntToBuffer(buffer, i, data);
				return i + 4;
			}
			if (count <= 271)
			{
				int data2 = Terrain.ReplaceLight(value, 15);
				WriteIntToBuffer(buffer, i, data2);
				buffer[i + 4] = (byte)(count - 16);
				return i + 5;
			}
			throw new InvalidOperationException("Count too large.");
		}
	}
}
