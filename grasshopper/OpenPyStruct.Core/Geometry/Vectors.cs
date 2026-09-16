namespace OpenPyStruct.Core.Geometry;

/// <summary>Minimal 3D vector so the builders need no RhinoCommon. The plugin converts at its edge.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Unit() { var l = Length; return l > 0 ? this * (1.0 / l) : this; }
    public double DistanceTo(Vec3 b) => (this - b).Length;
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);
}

/// <summary>2D point in the engine's analysis plane: x along the structure, y UP.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public double DistanceTo(Vec2 b) => Math.Sqrt((X - b.X) * (X - b.X) + (Y - b.Y) * (Y - b.Y));
}
