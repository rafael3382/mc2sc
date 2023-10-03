using System;

namespace Engine
{
	public struct Vector2 : IEquatable<Vector2>
	{
		public float X;

		public float Y;

		public static readonly Vector2 Zero = new Vector2(0f);

		public static readonly Vector2 One = new Vector2(1f);

		public static readonly Vector2 UnitX = new Vector2(1f, 0f);

		public static readonly Vector2 UnitY = new Vector2(0f, 1f);

		public Vector2 YX
		{
			get
			{
				return new Vector2(Y, X);
			}
			set
			{
				Y = value.X;
				X = value.Y;
			}
		}

		public Vector2(float v)
		{
			X = v;
			Y = v;
		}

		public Vector2(float x, float y)
		{
			X = x;
			Y = y;
		}

		public Vector2(Point2 p)
		{
			X = p.X;
			Y = p.Y;
		}

		public static implicit operator Vector2((float X, float Y) v)
		{
			return new Vector2(v.X, v.Y);
		}

		public override bool Equals(object obj)
		{
			if (!(obj is Vector2))
			{
				return false;
			}
			return Equals((Vector2)obj);
		}

		public override int GetHashCode()
		{
			return X.GetHashCode() + Y.GetHashCode();
		}

		public override string ToString()
		{
			return $"{X},{Y}";
		}

		public bool Equals(Vector2 other)
		{
			if (X == other.X)
			{
				return Y == other.Y;
			}
			return false;
		}

		public static Vector2 CreateFromAngle(float angle)
		{
			float y = MathUtils.Cos(angle);
			return new Vector2(0f - MathUtils.Sin(angle), y);
		}

		public static float Distance(Vector2 v1, Vector2 v2)
		{
			return MathUtils.Sqrt(DistanceSquared(v1, v2));
		}

		public static float DistanceSquared(Vector2 v1, Vector2 v2)
		{
			return MathUtils.Sqr(v1.X - v2.X) + MathUtils.Sqr(v1.Y - v2.Y);
		}

		public static float Dot(Vector2 v1, Vector2 v2)
		{
			return v1.X * v2.X + v1.Y * v2.Y;
		}

		public static float Cross(Vector2 v1, Vector2 v2)
		{
			return v1.X * v2.Y - v1.Y * v2.X;
		}

		public static Vector2 Perpendicular(Vector2 v)
		{
			return new Vector2(0f - v.Y, v.X);
		}

		public static Vector2 Rotate(Vector2 v, float angle)
		{
			float num = MathUtils.Cos(angle);
			float num2 = MathUtils.Sin(angle);
			return new Vector2(num * v.X + num2 * v.Y, (0f - num2) * v.X + num * v.Y);
		}

		public float Length()
		{
			return MathUtils.Sqrt(X * X + Y * Y);
		}

		public float LengthSquared()
		{
			return X * X + Y * Y;
		}

		public static Vector2 Floor(Vector2 v)
		{
			return new Vector2(MathUtils.Floor(v.X), MathUtils.Floor(v.Y));
		}

		public static Vector2 Ceiling(Vector2 v)
		{
			return new Vector2(MathUtils.Ceiling(v.X), MathUtils.Ceiling(v.Y));
		}

		public static Vector2 Round(Vector2 v)
		{
			return new Vector2(MathUtils.Round(v.X), MathUtils.Round(v.Y));
		}

		public static Vector2 Min(Vector2 v, float f)
		{
			return new Vector2(MathUtils.Min(v.X, f), MathUtils.Min(v.Y, f));
		}

		public static Vector2 Min(Vector2 v1, Vector2 v2)
		{
			return new Vector2(MathUtils.Min(v1.X, v2.X), MathUtils.Min(v1.Y, v2.Y));
		}

		public static Vector2 Max(Vector2 v, float f)
		{
			return new Vector2(MathUtils.Max(v.X, f), MathUtils.Max(v.Y, f));
		}

		public static Vector2 Max(Vector2 v1, Vector2 v2)
		{
			return new Vector2(MathUtils.Max(v1.X, v2.X), MathUtils.Max(v1.Y, v2.Y));
		}

		public static float MinElement(Vector2 v)
		{
			return MathUtils.Min(v.X, v.Y);
		}

		public static float MaxElement(Vector2 v)
		{
			return MathUtils.Max(v.X, v.Y);
		}

		public static Vector2 Clamp(Vector2 v, float min, float max)
		{
			return new Vector2(MathUtils.Clamp(v.X, min, max), MathUtils.Clamp(v.Y, min, max));
		}

		public static Vector2 Saturate(Vector2 v)
		{
			return new Vector2(MathUtils.Saturate(v.X), MathUtils.Saturate(v.Y));
		}

		public static Vector2 Lerp(Vector2 v1, Vector2 v2, float f)
		{
			return new Vector2(MathUtils.Lerp(v1.X, v2.X, f), MathUtils.Lerp(v1.Y, v2.Y, f));
		}

		public static Vector2 CatmullRom(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 v4, float f)
		{
			return new Vector2(MathUtils.CatmullRom(v1.X, v2.X, v3.X, v4.X, f), MathUtils.CatmullRom(v1.Y, v2.Y, v3.Y, v4.Y, f));
		}

		public static Vector2 Normalize(Vector2 v)
		{
			float num = v.Length();
			if (!(num > 0f))
			{
				return UnitX;
			}
			return v / num;
		}

		public static Vector2 LimitLength(Vector2 v, float maxLength)
		{
			float num = v.LengthSquared();
			if (num > maxLength * maxLength)
			{
				return v * (maxLength / MathUtils.Sqrt(num));
			}
			return v;
		}

		public static float Angle(Vector2 v1, Vector2 v2)
		{
			float num = MathUtils.Atan2(v1.Y, v1.X);
			float num2 = MathUtils.Atan2(v2.Y, v2.X) - num;
			if (num2 > (float)Math.PI)
			{
				num2 -= (float)Math.PI * 2f;
			}
			else if (num2 <= -(float)Math.PI)
			{
				num2 += (float)Math.PI * 2f;
			}
			return num2;
		}
		
		public static bool operator ==(Vector2 v1, Vector2 v2)
		{
			return v1.Equals(v2);
		}

		public static bool operator !=(Vector2 v1, Vector2 v2)
		{
			return !v1.Equals(v2);
		}

		public static Vector2 operator +(Vector2 v)
		{
			return v;
		}

		public static Vector2 operator -(Vector2 v)
		{
			return new Vector2(0f - v.X, 0f - v.Y);
		}

		public static Vector2 operator +(Vector2 v1, Vector2 v2)
		{
			return new Vector2(v1.X + v2.X, v1.Y + v2.Y);
		}

		public static Vector2 operator -(Vector2 v1, Vector2 v2)
		{
			return new Vector2(v1.X - v2.X, v1.Y - v2.Y);
		}

		public static Vector2 operator *(Vector2 v1, Vector2 v2)
		{
			return new Vector2(v1.X * v2.X, v1.Y * v2.Y);
		}

		public static Vector2 operator *(Vector2 v, float s)
		{
			return new Vector2(v.X * s, v.Y * s);
		}

		public static Vector2 operator *(float s, Vector2 v)
		{
			return new Vector2(v.X * s, v.Y * s);
		}

		public static Vector2 operator /(Vector2 v1, Vector2 v2)
		{
			return new Vector2(v1.X / v2.X, v1.Y / v2.Y);
		}

		public static Vector2 operator /(Vector2 v, float d)
		{
			float num = 1f / d;
			return new Vector2(v.X * num, v.Y * num);
		}
	}
}
