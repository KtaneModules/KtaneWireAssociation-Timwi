using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Rnd = System.Random;

namespace WireAssociation
{
    class WireMeshGenerator
    {
        const double _wireRadius = .0016;
        const double _wireRadiusHighlight = .0032;

        const double _wireMinBézierDeviation = .0025;
        const double _wireMaxBézierDeviation = .005;

        public static WireMeshes GenerateWire(double startX, double endX, double angle, bool top, bool highlight, int? seed = null)
        {
            const int bézierSteps = 8;
            const int tubeRevSteps = 16;
            const int subdivisions = 2;
            const int reserveForCopper = 3;

            var bottom = top ? .05 : -.05;

            var rnd = seed == null ? new Rnd() : new Rnd(seed.Value);

            // Generate outer Bézier curve and subdivide it
            var start = pt(startX, 0, 0);
            var end = pt(endX, 0, bottom);
            var startControl = pt(startX, 0, bottom / 2);
            var endControl = ((end + startControl) / 2);
            var rAngle = top ? -angle : angle;
            endControl = endControl.Rotate(pt(0, 0, 0), pt(1, 0, 0), rAngle);
            end = end.Rotate(pt(0, 0, 0), pt(1, 0, 0), rAngle);
            var curve = new[] { start, startControl, endControl, end };

            for (var i = 0; i < subdivisions; i++)
                curve = Enumerable.Range(0, (curve.Length - 1) / 3).SelectMany(sgm => subdivide(curve.Subarray(3 * sgm, 4))).Concat(new[] { end }).ToArray();

            // Apply deviations
            for (var i = 3; i < curve.Length - 1; i += 3)
            {
                var deviation = pt(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()).Normalize() * (rnd.NextDouble() * (_wireMaxBézierDeviation - _wireMinBézierDeviation) + _wireMinBézierDeviation);
                curve[i - 1] += deviation;
                curve[i + 1] -= deviation;
            }

            // Generate inner Bézier curves
            var points = Enumerable.Range(0, (curve.Length - 1) / 3).SelectMany(ix => bézier(curve[3 * ix], curve[3 * ix + 1], curve[3 * ix + 2], curve[3 * ix + 3], bézierSteps).SkipLast(1)).Concat(new[] { end }).ToArray();

            var generateEndCap = new Func<Pt, Pt, VertexInfo[], VertexInfo[][]>((sndLast, last, lastCircle) =>
            {
                var capCenter = last;
                var normal = capCenter - sndLast;
                return lastCircle.SelectConsecutivePairs(true, (v1, v2) => new[] { capCenter, v2.Point, v1.Point }.Select(p => new VertexInfo { Point = p, Normal = normal }).ToArray()).ToArray();
            });

            var generateMeshWithThickness = new Func<double, Pt[], Mesh>((thickness, pts) =>
            {
                var mainWire = tubeFromCurve(pts, thickness, tubeRevSteps);
                var endCap = generateEndCap(pts[pts.Length - 2], pts[pts.Length - 1], mainWire[mainWire.Length - 1]);
                return toMesh(createFaces(false, true, mainWire).Concat(endCap).ToArray());
            });

            return new WireMeshes
            {
                Wire = generateMeshWithThickness(_wireRadius, points.Subarray(0, points.Length - reserveForCopper)),
                Highlight = generateMeshWithThickness(_wireRadiusHighlight, points.Subarray(0, points.Length - reserveForCopper)),
                Copper = generateMeshWithThickness(_wireRadius / 2, points.Subarray(points.Length - reserveForCopper - 1, reserveForCopper + 1))
            };
        }

        private static Pt[] subdivide(Pt[] bézierPoints)
        {
            var a = bézierPoints[0];
            var b = bézierPoints[1];
            var c = bézierPoints[2];
            var d = bézierPoints[3];

            // Note: the final end-point is intentionally omitted
            return Ut.NewArray(
                a,
                a / 2 + b / 2,
                a / 4 + b / 2 + c / 4,
                a / 8 + 3 * b / 8 + 3 * c / 8 + d / 8,
                b / 4 + c / 2 + d / 4,
                c / 2 + d / 2);
        }

        sealed class VertexInfo
        {
            public Pt Point;
            public Pt Normal;
            public Vector3 V { get { return new Vector3((float) Point.X, (float) Point.Y, (float) Point.Z); } }
            public Vector3 N { get { return new Vector3((float) Normal.X, (float) Normal.Y, (float) Normal.Z); } }
        }

        private static Mesh toMesh(VertexInfo[][] triangles)
        {
            return new Mesh
            {
                vertices = triangles.SelectMany(t => t).Select(v => v.V).ToArray(),
                normals = triangles.SelectMany(t => t).Select(v => v.N).ToArray(),
                triangles = triangles.SelectMany(t => t).Select((v, i) => i).ToArray()
            };
        }

        // Converts a 2D array of vertices into triangles by joining each vertex with the next in each dimension
        private static VertexInfo[][] createFaces(bool closedX, bool closedY, VertexInfo[][] meshData)
        {
            var len = meshData[0].Length;
            return Enumerable.Range(0, meshData.Length).SelectManyConsecutivePairs(closedX, (i1, i2) =>
                Enumerable.Range(0, len).SelectManyConsecutivePairs(closedY, (j1, j2) => new[]
                {
                    // triangle 1
                    new[] { meshData[i1][j1], meshData[i2][j1], meshData[i2][j2] },
                    // triangle 2
                    new[] { meshData[i1][j1], meshData[i2][j2], meshData[i1][j2] }
                }))
                    .ToArray();
        }

        private static VertexInfo[][] tubeFromCurve(Pt[] pts, double radius, int revSteps)
        {
            var normals = new Pt[pts.Length];
            normals[0] = ((pts[1] - pts[0]) * pt(0, 1, 0)).Normalize() * radius;
            for (int i = 1; i < pts.Length - 1; i++)
                normals[i] = normals[i - 1].ProjectOntoPlane((pts[i + 1] - pts[i]) + (pts[i] - pts[i - 1])).Normalize() * radius;
            normals[pts.Length - 1] = normals[pts.Length - 2].ProjectOntoPlane(pts[pts.Length - 1] - pts[pts.Length - 2]).Normalize() * radius;

            var axes = pts.Select((p, i) =>
                i == 0 ? new { Start = pts[0], End = pts[1] } :
                i == pts.Length - 1 ? new { Start = pts[pts.Length - 2], End = pts[pts.Length - 1] } :
                new { Start = p, End = p + (pts[i + 1] - p) + (p - pts[i - 1]) }).ToArray();

            return Enumerable.Range(0, pts.Length)
                .Select(ix => new { Axis = axes[ix], Perpendicular = pts[ix] + normals[ix], Point = pts[ix] })
                .Select(inf => Enumerable.Range(0, revSteps)
                    .Select(i => 360 * i / revSteps)
                    .Select(angle => inf.Perpendicular.Rotate(inf.Axis.Start, inf.Axis.End, angle))
                    .Select(p => new VertexInfo { Point = p, Normal = p - inf.Point }).Reverse().ToArray())
                .ToArray();
        }

        private static IEnumerable<Pt> bézier(Pt start, Pt control1, Pt control2, Pt end, int steps)
        {
            return Enumerable.Range(0, steps)
                .Select(i => (double) i / (steps - 1))
                .Select(t => pow(1 - t, 3) * start + 3 * pow(1 - t, 2) * t * control1 + 3 * (1 - t) * t * t * control2 + pow(t, 3) * end);
        }

        static double sin(double x)
        {
            return Math.Sin(x * Math.PI / 180);
        }

        static double cos(double x)
        {
            return Math.Cos(x * Math.PI / 180);
        }

        static double pow(double x, double y)
        {
            return Math.Pow(x, y);
        }

        static Pt pt(double x, double y, double z)
        {
            return new Pt(x, y, z);
        }
    }
}
