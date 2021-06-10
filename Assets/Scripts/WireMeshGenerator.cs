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

        public static WireMeshes GenerateWire(double startX, double endX, double angle, bool isAtTop, bool highlight, int seed)
        {
            const int bézierSteps = 6;
            const int tubeRevSteps = 7;
            const int subdivisions = 1;
            const int reserveForCopper = 1;

            var bottom = isAtTop ? .05 : -.05;
            var top = isAtTop ? -.001f : .001f;

            var rnd = new Rnd(seed);

            // Generate outer Bézier curve and subdivide it
            var start = pt(startX, 0, top);
            var end = pt(endX, 0, bottom);
            var startControl = pt(startX, 0, bottom / 2);
            var endControl = ((end + startControl) / 2);
            var rAngle = isAtTop ? -angle : angle;
            endControl = endControl.Rotate(pt(0, 0, 0), pt(1, 0, 0), rAngle);
            end = end.Rotate(pt(0, 0, 0), pt(1, 0, 0), rAngle);
            var curve = new[] { start, startControl, endControl, end };

            for (var i = 0; i < subdivisions; i++)
                curve = Enumerable.Range(0, (curve.Length - 1) / 3).SelectMany(sgm => subdivide(curve.Subarray(3 * sgm, 4))).Concat(new[] { end }).ToArray();

            // Apply deviations
            for (var i = 0; i < curve.Length; i += 3)
            {
                var deviation = pt(rnd.NextDouble() - .5, rnd.NextDouble() - .5, rnd.NextDouble() - .5).Normalize() * (rnd.NextDouble() * (_wireMaxBézierDeviation - _wireMinBézierDeviation) + _wireMinBézierDeviation);
                if (i > 0)
                    curve[i - 1] += deviation;
                if (i < curve.Length - 1)
                    curve[i + 1] -= deviation;
            }

            // Generate inner Bézier curves
            var points = Enumerable.Range(0, (curve.Length - 1) / 3).SelectMany(ix => bézier(curve[3 * ix], curve[3 * ix + 1], curve[3 * ix + 2], curve[3 * ix + 3], bézierSteps).SkipLast(1)).Concat(new[] { end }).ToArray();

            var generateMeshWithThickness = new Func<double, Pt[], Mesh>((thickness, pts) =>
            {
                var mainWire = tubeFromCurve(pts, thickness, tubeRevSteps);
                var endCap = mainWire[mainWire.Length - 1].SelectConsecutivePairs(true, (v1, v2) => (new[] { pts[pts.Length - 1], v2.Point, v1.Point }).Select(p => new VertexInfo { Point = p, Normal = pts[pts.Length - 1] - pts[pts.Length - 2] }).ToArray()).ToArray();
                return toMesh(createFaces(false, true, mainWire).Concat(endCap).ToArray());
            });

            return new WireMeshes
            {
                Wire = generateMeshWithThickness(_wireRadius, points.Subarray(0, points.Length - reserveForCopper)),
                Highlight = generateMeshWithThickness(_wireRadiusHighlight, points.Subarray(0, points.Length - reserveForCopper)),
                Copper = generateMeshWithThickness(_wireRadius / 2, points.Subarray(points.Length - reserveForCopper - 2, reserveForCopper + 2))
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
                .Select(t => Math.Pow(1 - t, 3) * start + 3 * Math.Pow(1 - t, 2) * t * control1 + 3 * (1 - t) * t * t * control2 + Math.Pow(t, 3) * end);
        }

        private static Pt pt(double x, double y, double z)
        {
            return new Pt(x, y, z);
        }
    }
}
