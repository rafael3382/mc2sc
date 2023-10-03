using System;
using System.Collections.Generic;
using System.IO;
using Engine;

namespace Game
{
	public class TerrainSerializer14 : IDisposable
	{
		public const int MaxChunks = 65536;

		public const string ChunksFileName = "Chunks.dat";

		public byte[] m_buffer = new byte[131072];

		public Dictionary<Point2, int> m_chunkOffsets = new Dictionary<Point2, int>();

		public Stream m_stream;

		public TerrainSerializer14(string directoryName)
		{
			string path = Path.Combine(directoryName, "Chunks.dat");
			if (!File.Exists(path))
			{
				using (Stream stream = File.Open(path, FileMode.Create))
				{
					for (int i = 0; i < 65537; i++)
					{
						WriteTOCEntry(stream, 0, 0, 0);
					}
				}
			}
			m_stream = File.Open(path, FileMode.OpenOrCreate);
			while (true)
			{
				ReadTOCEntry(m_stream, out var cx, out var cz, out var offset);
				if (offset != 0)
				{
					m_chunkOffsets[new Point2(cx, cz)] = offset;
					continue;
				}
				break;
			}
		}

		public bool LoadChunk(TerrainChunk chunk)
		{
			return LoadChunkBlocks(chunk);
		}

		public void SaveChunk(TerrainChunk chunk)
		{
			SaveChunkBlocks(chunk);
		}

		public void Dispose()
		{
		    m_stream.Close();
		}

		public static void ReadChunkHeader(Stream stream)
		{
			int num = ReadInt(stream);
			int num2 = ReadInt(stream);
			ReadInt(stream);
			ReadInt(stream);
			if (num != -559038737 || num2 != -1)
			{
				throw new InvalidOperationException("Invalid chunk header.");
			}
		}

		public static void WriteChunkHeader(Stream stream, int cx, int cz)
		{
			WriteInt(stream, -559038737);
			WriteInt(stream, -1);
			WriteInt(stream, cx);
			WriteInt(stream, cz);
		}

		public static void ReadTOCEntry(Stream stream, out int cx, out int cz, out int offset)
		{
			cx = ReadInt(stream);
			cz = ReadInt(stream);
			offset = ReadInt(stream);
		}

		public static void WriteTOCEntry(Stream stream, int cx, int cz, int offset)
		{
			WriteInt(stream, cx);
			WriteInt(stream, cz);
			WriteInt(stream, offset);
		}

		public bool LoadChunkBlocks(TerrainChunk chunk)
		{
			bool result = false;
			int num = chunk.Origin.X >> 4;
			int num2 = chunk.Origin.Y >> 4;
			try
			{
				if (m_chunkOffsets.TryGetValue(new Point2(num, num2), out var value))
				{
					m_stream.Seek(value, SeekOrigin.Begin);
					ReadChunkHeader(m_stream);
					int num3 = 0;
					m_stream.Read(m_buffer, 0, 131072);
					for (int i = 0; i < 16; i++)
					{
						for (int j = 0; j < 16; j++)
						{
							int num4 = TerrainChunk.CalculateCellIndex(i, 0, j);
							for (int k = 0; k < 256; k++)
							{
								int num5 = m_buffer[num3++];
								num5 |= m_buffer[num3++] << 8;
								chunk.SetCellValueFast(num4++, num5);
							}
						}
					}
					num3 = 0;
					m_stream.Read(m_buffer, 0, 1024);
					for (int l = 0; l < 16; l++)
					{
						for (int m = 0; m < 16; m++)
						{
							int num6 = m_buffer[num3++];
							num6 |= m_buffer[num3++] << 8;
							num6 |= m_buffer[num3++] << 16;
							num6 |= m_buffer[num3++] << 24;
							chunk.SetShaftValueFast(l, m, num6);
						}
					}
					result = true;
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error loading data for chunk ({num},{num2}).");
				Console.WriteLine(e);
			}
			return result;
		}

		public void SaveChunkBlocks(TerrainChunk chunk)
		{
			int num = chunk.Origin.X >> 4;
			int num2 = chunk.Origin.Y >> 4;
			try
			{
				bool flag = false;
				if (m_chunkOffsets.TryGetValue(new Point2(num, num2), out var value))
				{
					m_stream.Seek(value, SeekOrigin.Begin);
				}
				else
				{
					flag = true;
					value = (int)m_stream.Length;
					m_stream.Seek(value, SeekOrigin.Begin);
				}
				WriteChunkHeader(m_stream, num, num2);
				int num3 = 0;
				for (int i = 0; i < 16; i++)
				{
					for (int j = 0; j < 16; j++)
					{
						int num4 = TerrainChunk.CalculateCellIndex(i, 0, j);
						for (int k = 0; k < 256; k++)
						{
							int cellValueFast = chunk.GetCellValueFast(num4++);
							m_buffer[num3++] = (byte)cellValueFast;
							m_buffer[num3++] = (byte)(cellValueFast >> 8);
						}
					}
				}
				m_stream.Write(m_buffer, 0, 131072);
				num3 = 0;
				for (int l = 0; l < 16; l++)
				{
					for (int m = 0; m < 16; m++)
					{
						int shaftValue = chunk.GetShaftValueFast(l, m);
						m_buffer[num3++] = (byte)shaftValue;
						m_buffer[num3++] = (byte)(shaftValue >> 8);
						m_buffer[num3++] = (byte)(shaftValue >> 16);
						m_buffer[num3++] = (byte)(shaftValue >> 24);
					}
				}
				m_stream.Write(m_buffer, 0, 1024);
				if (flag)
				{
					m_stream.Flush();
					int num5 = m_chunkOffsets.Count % 65536 * 3 * 4;
					m_stream.Seek(num5, SeekOrigin.Begin);
					WriteInt(m_stream, num);
					WriteInt(m_stream, num2);
					WriteInt(m_stream, value);
					m_chunkOffsets[new Point2(num, num2)] = value;
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error writing data for chunk ({num},{num2}).");
				Console.WriteLine(e);
			}
		}

		public static int ReadInt(Stream stream)
		{
			return stream.ReadByte() + (stream.ReadByte() << 8) + (stream.ReadByte() << 16) + (stream.ReadByte() << 24);
		}

		public static void WriteInt(Stream stream, int value)
		{
			stream.WriteByte((byte)((uint)value & 0xFFu));
			stream.WriteByte((byte)((uint)(value >> 8) & 0xFFu));
			stream.WriteByte((byte)((uint)(value >> 16) & 0xFFu));
			stream.WriteByte((byte)((uint)(value >> 24) & 0xFFu));
		}
	}
}
