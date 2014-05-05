using System;
using System.Collections.Generic;
using System.IO;
using ClipperLib;
using LeagueSharp;
using SharpDX;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;


namespace EvadeSharp
{
    internal class Utils
    {
        public static Vector2 perpendicular(Vector2 v)
        {
            return new Vector2(-v.Y, v.X);
        }

        public static Vector2 perpendicular2(Vector2 v)
        {
            return new Vector2(v.Y, -v.X);
        }

        public static Vector2 To2D(Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        public static Vector3 To3D(Vector2 v)
        {
            return new Vector3(v.X, v.Y, ObjectManager.Player.ServerPosition.Z);
        }

        public static bool IsValidVector2(Vector2 vector)
        {
            if (vector.X.CompareTo(0.0f) == 0 && vector.Y.CompareTo(0.0f) == 0)
            {
                return false;
            }
            return true;
        }

        public static List<List<Vector2>> ClipperPathsToPolygons(Paths Pols)
        {
            var Result = new List<List<Vector2>>();

            foreach (var Polygon in Pols)
            {
                Result.Add(ClipperPathToPolygon(Polygon));
            }

            return Result;
        }

        public static List<Vector2> ClipperPathToPolygon(Path Pol)
        {
            var Result = new List<Vector2>();

            foreach (IntPoint point in Pol)
            {
                Result.Add(new Vector2(point.X, point.Y));
            }

            return Result;
        }

        public static float GetDistanceToPolygons(List<List<Vector2>> Polygons, Vector2 point)
        {
            var Candidates = new List<Vector2>();
            foreach (var Polygon in Polygons)
            {
                for (int i = 0; i < Polygon.Count; i++)
                {
                    Vector2 A = Polygon[i];
                    Vector2 B = Polygon[(i == Polygon.Count - 1) ? 0 : (i + 1)];

                    Object[] objects1 = Utils.VectorPointProjectionOnLineSegment(A, B,
                        Utils.To2D(ObjectManager.Player.ServerPosition));
                    var Candidate = (Vector2)objects1[0];
                    Candidates.Add(Candidate);
                }
            }

            Vector2 v = GetClosestVector(point, Candidates);

            return IsValidVector2(v) ? Vector2.Distance(v, point) : float.MaxValue;
        }

        public static Vector2 GetClosestVector(Vector2 from, List<Vector2> vList)
        {
            if (vList.Count == 1)
            {
                return vList[0];
            }
            float MinDistance = -1;
            var MinDistanceV = new Vector2();

            foreach (Vector2 test in vList)
            {
                float Distance = Vector2.DistanceSquared(test, from);
                if (Distance < MinDistance || MinDistance == -1)
                {
                    MinDistanceV = test;
                    MinDistance = Distance;
                }
            }
            return MinDistanceV;
        }

        public static Obj_AI_Base GetClosestUnit(Vector2 from, List<Obj_AI_Base> uList)
        {
            if (uList.Count == 1)
            {
                return uList[0];
            }

            float MinDistance = -1;
            var MinDistanceU = new Obj_AI_Base();

            foreach (Obj_AI_Base test in uList)
            {
                float Distance = Vector2.DistanceSquared(Utils.To2D(test.ServerPosition), from);
                if (Distance < MinDistance || MinDistance == -1)
                {
                    MinDistanceU = test;
                    MinDistance = Distance;
                }
            }

            return MinDistanceU;
        }

        public static Vector2 Vector2Rotate(Vector2 v, float angle)
        {
            double c;
            double s;
            c = Math.Cos(angle);
            s = Math.Sin(angle);

            return new Vector2((float)(v.X * c - v.Y * s), (float)(v.Y * c + v.X * s));
        }

        public static bool Ccw(Vector2 A, Vector2 B, Vector2 C)
        {
            return (C.Y - A.Y) * (B.X - A.X) > (B.Y - A.Y) * (C.X - A.X);
        }

        public static bool Intersect(Vector2 A, Vector2 B, Vector2 C, Vector2 D)
        {
            return Ccw(A, C, D) != Ccw(B, C, D) && Ccw(A, B, C) != Ccw(A, B, D);
        }

        public static Vector2 LineSegmentIntersection(Vector2 A, Vector2 B, Vector2 C, Vector2 D)
        {
            if (Intersect(A, B, C, D))
            {
                return VectorIntersection(A, B, C, D);
            }
            return new Vector2();
        }

        public static List<Vector2> LineSegmentPathIntersections(List<Vector2> path, Vector2 C, Vector2 D)
        {
            var Result = new List<Vector2>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 A = path[i];
                Vector2 B = path[i + 1];
                Vector2 Intersection = LineSegmentIntersection(A, B, C, D);
                if (IsValidVector2(Intersection))
                {
                    Result.Add(Intersection);
                }
            }
            return Result;
        }

        public static Object[] VectorPointProjectionOnLineSegment(Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float cx = v3.X;
            float cy = v3.Y;
            float ax = v1.X;
            float ay = v1.Y;
            float bx = v2.X;
            float by = v2.Y;
            float rL = ((cx - ax) * (bx - ax) + (cy - ay) * (by - ay)) /
                       ((float)Math.Pow(bx - ax, 2) + (float)Math.Pow(by - ay, 2));
            var pointLine = new Vector2(ax + rL * (bx - ax), ay + rL * (by - ay));
            float rS;
            if (rL < 0)
            {
                rS = 0;
            }
            else if (rL > 1)
            {
                rS = 1;
            }
            else
            {
                rS = rL;
            }
            bool isOnSegment;
            if (rS.CompareTo(rL) == 0)
            {
                isOnSegment = true;
            }
            else
            {
                isOnSegment = false;
            }
            var pointSegment = new Vector2();
            if (isOnSegment)
            {
                pointSegment = pointLine;
            }
            else
            {
                pointSegment = new Vector2(ax + rS * (bx - ax), ay + rS * (by - ay));
            }
            return new object[3] { pointSegment, pointLine, isOnSegment };
        }

        public static Vector2 VectorIntersection(Vector2 a1, Vector2 b1, Vector2 a2, Vector2 b2)
        {
            float x1 = a1.X, y1 = a1.Y, x2 = b1.X, y2 = b1.Y, x3 = a2.X, y3 = a2.Y, x4 = b2.X, y4 = b2.Y;
            float r = x1 * y2 - y1 * x2, s = x3 * y4 - y3 * x4, u = x3 - x4, v = x1 - x2, k = y3 - y4, l = y1 - y2;
            float px = r * u - v * s, py = r * k - l * s, divisor = v * k - l * u;
            if (divisor.CompareTo(0) != 0)
            {
                return new Vector2(px / divisor, py / divisor);
            }
            return new Vector2();
        }

        public static List<Vector2> GetMyPath(Vector2 destination)
        {
            var Result = new List<Vector2>();
            foreach (Vector3 point in ObjectManager.Player.GetPath(To3D(destination)))
            {
                Result.Add(To2D(point));
            }
            return Result;
        }

        public static List<Vector2> GetWaypoints(Obj_AI_Hero unit)
        {
            var Result = new List<Vector2>();
            Result.Add(To2D(unit.ServerPosition));
            foreach (Vector3 point in unit.Path)
            {
                Result.Add(To2D(point));
            }

            return Result;
        }

        public static Vector2 CutPath(List<Vector2> path, float Distance)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                float Dist = Vector2.Distance(path[i], path[i + 1]);
                if (Dist > Distance)
                {
                    Vector2 Direction = (path[i + 1] - path[i]);
                    Direction.Normalize();
                    return path[i] + Distance * Direction;
                }
                Distance = Distance - Dist;
            }
            return path[path.Count - 1];
        }

        public static float DistanceToPointInPath(List<Vector2> path, Vector2 point, bool MaxWorstCase)
        {
            float WDist = 0f;

            for (int i = 0; i < path.Count - 1; i++)
            {
                Object[] objects1 = VectorPointProjectionOnLineSegment(path[i], path[i + 1], point);
                var pointSegment1 = (Vector2)objects1[0];
                var pointLine1 = (Vector2)objects1[1];
                var isOnSegment1 = (bool)objects1[2];


                if (Vector2.DistanceSquared(pointSegment1, point) < 75 * 75) //Maybe not the best solution
                {
                    return WDist + Vector2.Distance(path[i], point);
                }

                WDist = WDist + Vector2.Distance(path[i], path[i + 1]);
            }


            return MaxWorstCase ? WDist : 0;
        }

        public static void SendMoveToPacket(Vector2 point)
        {
            var memoryStream = new MemoryStream();
            var binaryWriter = new BinaryWriter(memoryStream);

            binaryWriter.Write((byte)113); //Header
            binaryWriter.Write(ObjectManager.Player.NetworkId); //Source Network ID
            binaryWriter.Write((byte)2); //Type
            binaryWriter.Write(point.X); //X
            binaryWriter.Write(point.Y); //Y
            binaryWriter.Write(0.00f); //Target Network ID
            binaryWriter.Write((byte)0); // Always 0
            binaryWriter.Write((float)ObjectManager.Player.NetworkId); //Unit Network ID
            binaryWriter.Write((byte)0); //WayPointCount * 2

            Game.SendPacket(memoryStream.ToArray(), PacketChannel.C2S, PacketProtocolFlags.Reliable);
        }

        public static void DrawCircle(Vector3 point, float width, SharpDX.Color c)
        {
            for (int i = 0; i <= 30; i++)
            {
                float X = point.X + width * (float)Math.Cos(2 * Math.PI / 30 * i);
                float Y = point.Y + width * (float)Math.Sin(2 * Math.PI / 30 * i);

                float[] p1 = Drawing.WorldToScreen(Utils.To3D(new Vector2(X, Y)));
                float X2 = point.X + width * (float)Math.Cos(2 * Math.PI / 30 * (i + 1));
                float Y2 = point.Y + width * (float)Math.Sin(2 * Math.PI / 30 * (i + 1));

                float[] p2 = Drawing.WorldToScreen(Utils.To3D(new Vector2(X2, Y2)));

                Drawing.DrawLine(p1[0], p1[1], p2[0], p2[1], 1, System.Drawing.Color.Wheat);
            }
        }
    }
}

