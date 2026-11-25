using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IGIEditor
{
    /// <summary>A point in projected viewport space.</summary>
    public struct TerrainPoint
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Depth;

        public TerrainPoint(double x, double y, double depth)
        {
            X = x;
            Y = y;
            Depth = depth;
        }
    }

    /// <summary>A point on the height map plane, in grid coordinates.</summary>
    public struct TerrainWorldPoint
    {
        public readonly double X;
        public readonly double Y;

        public TerrainWorldPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Orbit camera and orthographic projection used by every preview.</summary>
    public sealed class TerrainCamera
    {
        public double YawDegrees = 45.0;
        public double PitchDegrees = 55.0;
        public double Zoom = 1.0;
        public double VerticalScale = 8.0;
        public int Width = 720;
        public int Height = 480;
        public double Center = 16.0;

        public TerrainPoint Project(double x, double y, double heightMeters)
        {
            double yaw = YawDegrees * Math.PI / 180.0;
            double pitch = PitchDegrees * Math.PI / 180.0;
            double scale = Scale;
            double x0 = x - Center;
            double y0 = y - Center;
            double xr = x0 * Math.Cos(yaw) - y0 * Math.Sin(yaw);
            double yr = x0 * Math.Sin(yaw) + y0 * Math.Cos(yaw);
            double z = heightMeters * VerticalScale;
            double sx = xr * scale + Width / 2.0;
            double sy = (yr * Math.Sin(pitch) - z * Math.Cos(pitch)) * scale + Height / 2.0;
            double depth = yr * Math.Cos(pitch) + z * Math.Sin(pitch);
            return new TerrainPoint(sx, sy, depth);
        }

        /// <summary>Inverse projection onto a horizontal plane, used for click-to-sculpt.</summary>
        public TerrainWorldPoint ScreenToTerrain(double screenX, double screenY, double planeHeightMeters)
        {
            double yaw = YawDegrees * Math.PI / 180.0;
            double pitch = PitchDegrees * Math.PI / 180.0;
            double sinPitch = Math.Sin(pitch);
            if (Math.Abs(sinPitch) < 0.001)
                throw new InvalidOperationException("Cannot pick terrain when the camera is horizontal.");

            double u = (screenX - Width / 2.0) / Scale;
            double v = (screenY - Height / 2.0) / Scale;
            double z = planeHeightMeters * VerticalScale;
            double yr = (v + z * Math.Cos(pitch)) / sinPitch;
            double xr = u;
            double x = xr * Math.Cos(yaw) + yr * Math.Sin(yaw) + Center;
            double y = -xr * Math.Sin(yaw) + yr * Math.Cos(yaw) + Center;
            return new TerrainWorldPoint(x, y);
        }

        private double Scale
        {
            get { return Zoom * Math.Min(Width, Height) / Math.Max(2.0, 2.0 * Center + 2.0); }
        }
    }

    /// <summary>One projected terrain triangle with a simple diffuse shade.</summary>
    public sealed class TerrainFace
    {
        public TerrainPoint A;
        public TerrainPoint B;
        public TerrainPoint C;
        public double Depth;
        public int Shade;
        public int Order;
    }

    /// <summary>Projection/mesh code shared by the WinForms viewport and the SVG preview.</summary>
    public static class TerrainRenderer
    {
        public static List<TerrainFace> BuildFaces(TerrainHeightmap map, TerrainCamera camera)
        {
            if (map == null) throw new ArgumentNullException("map");
            if (camera == null) throw new ArgumentNullException("camera");

            List<TerrainFace> faces = new List<TerrainFace>(map.Size * map.Size * 2);
            int order = 0;
            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    double z00 = map.HeightMeters(x, y);
                    double z10 = map.HeightMeters(x + 1, y);
                    double z11 = map.HeightMeters(x + 1, y + 1);
                    double z01 = map.HeightMeters(x, y + 1);

                    AddFace(faces, camera, x, y, z00, x + 1, y, z10, x + 1, y + 1, z11, ref order);
                    AddFace(faces, camera, x, y, z00, x + 1, y + 1, z11, x, y + 1, z01, ref order);
                }
            }

            faces.Sort(delegate (TerrainFace a, TerrainFace b)
            {
                int cmp = b.Depth.CompareTo(a.Depth); // far to near (painter's algorithm)
                return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
            });
            return faces;
        }

        /// <summary>Deterministic, headless 3D preview used for verification and documentation.</summary>
        public static string RenderSvg(TerrainHeightmap map, TerrainCamera camera)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(camera.Width.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" height=\"").Append(camera.Height.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" viewBox=\"0 0 ").Append(camera.Width.ToString(CultureInfo.InvariantCulture)).Append(' ');
            sb.Append(camera.Height.ToString(CultureInfo.InvariantCulture)).Append("\">\n");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#dce9f2\"/>\n");

            foreach (TerrainFace face in BuildFaces(map, camera))
            {
                sb.Append("<polygon points=\"");
                AppendPoint(sb, face.A);
                sb.Append(' ');
                AppendPoint(sb, face.B);
                sb.Append(' ');
                AppendPoint(sb, face.C);
                sb.Append("\" fill=\"rgb(").Append(face.Shade.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(face.Shade.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(face.Shade.ToString(CultureInfo.InvariantCulture)).Append(")\"/>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static void AddFace(List<TerrainFace> faces, TerrainCamera camera,
                                    double ax, double ay, double az,
                                    double bx, double by, double bz,
                                    double cx, double cy, double cz, ref int order)
        {
            TerrainFace face = new TerrainFace();
            face.A = camera.Project(ax, ay, az);
            face.B = camera.Project(bx, by, bz);
            face.C = camera.Project(cx, cy, cz);
            face.Depth = (face.A.Depth + face.B.Depth + face.C.Depth) / 3.0;
            face.Shade = Shade(ax, ay, az, bx, by, bz, cx, cy, cz, camera.VerticalScale);
            face.Order = order++;
            faces.Add(face);
        }

        private static int Shade(double ax, double ay, double az, double bx, double by, double bz,
                                 double cx, double cy, double cz, double verticalScale)
        {
            double ux = bx - ax, uy = by - ay, uz = (bz - az) * verticalScale;
            double vx = cx - ax, vy = cy - ay, vz = (cz - az) * verticalScale;
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9) return 128;
            nz = Math.Abs(nz) / len; // orientation-independent terrain normal
            int shade = (int)Math.Round(48 + 190 * nz);
            return TerrainMath.Clamp(shade, 0, 255);
        }

        private static void AppendPoint(StringBuilder sb, TerrainPoint p)
        {
            sb.Append(p.X.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.Y.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }
}
