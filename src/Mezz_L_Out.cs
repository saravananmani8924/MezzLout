using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using Tekla.Structures.Plugins;

namespace Mezz_L_Out
{
    public class StructuresData
    {
        [StructuresField("XCordSpacing")] public string XCordSpacing;
        [StructuresField("YCordSpacing")] public string YCordSpacing;
        [StructuresField("JoistDirec")] public int JoistDirec;
        [StructuresField("JoistSp1")] public string JoistSp1;
        [StructuresField("JoistSp2")] public string JoistSp2;
        [StructuresField("JoistSp3")] public string JoistSp3;
        [StructuresField("JoistSp4")] public string JoistSp4;
        [StructuresField("JoistSp5")] public string JoistSp5;
        [StructuresField("JoistSp6")] public string JoistSp6;
        [StructuresField("JoistSp7")] public string JoistSp7;
        [StructuresField("JoistSp8")] public string JoistSp8;
        [StructuresField("JoistSp9")] public string JoistSp9;
        [StructuresField("JoistSp10")] public string JoistSp10;
        [StructuresField("JoistSp11")] public string JoistSp11;

        [StructuresField("HorBeamSec")] public string HorBeamSec;
        [StructuresField("VerBeamSec")] public string VerBeamSec;
        [StructuresField("JoistBeamSec")] public string JoistBeamSec;
        [StructuresField("ColumnSec")] public string ColumnSec;

        [StructuresField("Jconnectiontype")] public int Jconnectiontype;
        [StructuresField("JConOrient")] public int JConOrient;
        [StructuresField("JConStiffDep")] public int JConStiffDep;

        [StructuresField("ConnSettings")] public string ConnSettings;
        [StructuresField("MezzLevel")] public double MezzLevel;
    }

    public enum JoistDirection
    {
        AlongX = 0,
        AlongY = 1
    }

    [Plugin("Mezz_L_Out")]
    [PluginUserInterface("Mezz_L_Out.MainForm")]
    public class Mezz_L_Out : PluginBase
    {
        private const double Tol = 0.5; // mm

        private readonly Model _model;
        private readonly StructuresData _data;

        public Mezz_L_Out(StructuresData data)
        {
            _model = new Model();
            _data = data;
        }

        public override List<InputDefinition> DefineInput()
        {
            var picker = new Picker();
            var start = picker.PickPoint("Pick mezzanine start point");
            var end = picker.PickPoint("Pick mezzanine end point");
            return new List<InputDefinition>
            {
                new InputDefinition(start),
                new InputDefinition(end)
            };
        }

        public override bool Run(List<InputDefinition> input)
        {
            if (!_model.GetConnectionStatus() || input == null || input.Count < 2)
            {
                return false;
            }

            var start = (Point)input[0].GetInput();
            var end = (Point)input[1].GetInput();

            var xDir = NormalizeOrDefault(new Vector(end.X - start.X, end.Y - start.Y, 0), new Vector(1, 0, 0));
            var globalZ = new Vector(0, 0, 1);
            var yDir = NormalizeOrDefault(Cross(globalZ, xDir), new Vector(0, 1, 0));
            var plane = new CoordinateSystem(start, xDir, yDir);

            var xSpacing = ParseSpacing(_data.XCordSpacing);
            var ySpacing = ParseSpacing(_data.YCordSpacing);
            var joistSpacing = ParseSpacing(string.Join(" ", GetJoistSpacingRows()));

            if (xSpacing.Count == 0 || ySpacing.Count == 0 || joistSpacing.Count == 0)
            {
                return false;
            }

            var xStations = BuildStations(0.0, xSpacing);
            var yStations = BuildStations(0.0, ySpacing);
            var mezzLevel = _data.MezzLevel;

            var horizontalBeams = InsertMainBeams(plane, xStations, yStations, true, _data.HorBeamSec, mezzLevel);
            var verticalBeams = InsertMainBeams(plane, xStations, yStations, false, _data.VerBeamSec, mezzLevel);

            var allPrimaryBeams = horizontalBeams.Concat(verticalBeams).ToList();
            var joists = InsertJoists(plane, xStations, yStations, joistSpacing, mezzLevel, (JoistDirection)_data.JoistDirec);

            CreateJoistEndConnections(joists, allPrimaryBeams, _data.ConnSettings);

            var columns = InsertColumnsAtFourBeamNodes(plane, xStations, yStations, mezzLevel, _data.ColumnSec);
            CreateBeamToColumnConnections(horizontalBeams, verticalBeams, columns);

            _model.CommitChanges("Generated parametric mezzanine layout");
            return true;
        }

        private IEnumerable<string> GetJoistSpacingRows()
        {
            return new[]
            {
                _data.JoistSp1, _data.JoistSp2, _data.JoistSp3, _data.JoistSp4, _data.JoistSp5,
                _data.JoistSp6, _data.JoistSp7, _data.JoistSp8, _data.JoistSp9, _data.JoistSp10,
                _data.JoistSp11
            }.Where(s => !string.IsNullOrWhiteSpace(s));
        }

        private static List<double> ParseSpacing(string raw)
        {
            var result = new List<double>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            var tokens = raw.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.Contains("*"))
                {
                    var parts = token.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var count) && TryParseDouble(parts[1], out var pitch))
                    {
                        for (var i = 0; i < count; i++) result.Add(pitch);
                    }
                }
                else if (TryParseDouble(token, out var direct))
                {
                    result.Add(direct);
                }
            }

            return result.Where(v => v > 0).ToList();
        }

        private static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static List<double> BuildStations(double origin, IReadOnlyList<double> spacing)
        {
            var stations = new List<double> { origin };
            var cursor = origin;
            foreach (var sp in spacing)
            {
                cursor += sp;
                stations.Add(cursor);
            }

            return stations;
        }

        private List<Beam> InsertMainBeams(
            CoordinateSystem cs,
            IReadOnlyList<double> xStations,
            IReadOnlyList<double> yStations,
            bool horizontal,
            string profile,
            double z)
        {
            var beams = new List<Beam>();
            if (horizontal)
            {
                foreach (var y in yStations)
                {
                    var start = LocalToGlobal(cs, xStations.First(), y, z);
                    var end = LocalToGlobal(cs, xStations.Last(), y, z);
                    beams.Add(CreateBeam(start, end, profile, "1"));
                }
            }
            else
            {
                foreach (var x in xStations)
                {
                    var start = LocalToGlobal(cs, x, yStations.First(), z);
                    var end = LocalToGlobal(cs, x, yStations.Last(), z);
                    beams.Add(CreateBeam(start, end, profile, "2"));
                }
            }

            return beams;
        }

        private List<Beam> InsertJoists(
            CoordinateSystem cs,
            IReadOnlyList<double> xStations,
            IReadOnlyList<double> yStations,
            IReadOnlyList<double> joistSpacing,
            double z,
            JoistDirection direction)
        {
            var joists = new List<Beam>();
            var cumulative = BuildStations(0, joistSpacing);

            if (direction == JoistDirection.AlongX)
            {
                foreach (var y in cumulative.Where(v => v <= yStations.Last() + Tol))
                {
                    for (var i = 0; i < xStations.Count - 1; i++)
                    {
                        var start = LocalToGlobal(cs, xStations[i], y, z);
                        var end = LocalToGlobal(cs, xStations[i + 1], y, z);
                        joists.Add(CreateBeam(start, end, _data.JoistBeamSec, "3"));
                    }
                }
            }
            else
            {
                foreach (var x in cumulative.Where(v => v <= xStations.Last() + Tol))
                {
                    for (var i = 0; i < yStations.Count - 1; i++)
                    {
                        var start = LocalToGlobal(cs, x, yStations[i], z);
                        var end = LocalToGlobal(cs, x, yStations[i + 1], z);
                        joists.Add(CreateBeam(start, end, _data.JoistBeamSec, "3"));
                    }
                }
            }

            return joists;
        }

        private static Beam CreateBeam(Point start, Point end, string profile, string clazz)
        {
            var beam = new Beam(Beam.BeamTypeEnum.BEAM)
            {
                StartPoint = start,
                EndPoint = end,
                Name = "MEZZ_MEMBER"
            };

            beam.Profile.ProfileString = string.IsNullOrWhiteSpace(profile) ? "I200*100*5.5*8" : profile;
            beam.Material.MaterialString = "S355";
            beam.Class = clazz;
            beam.Position.Plane = Position.PlaneEnum.MIDDLE;
            beam.Position.Rotation = Position.RotationEnum.TOP;
            beam.Position.Depth = Position.DepthEnum.MIDDLE;
            beam.Insert();
            return beam;
        }

        private void CreateJoistEndConnections(List<Beam> joists, List<Beam> primaryBeams, string settingsFile)
        {
            var created = new HashSet<string>();

            foreach (var joist in joists)
            {
                var ends = new[] { joist.StartPoint, joist.EndPoint };
                foreach (var end in ends)
                {
                    var supporting = FindSupportingBeam(end, primaryBeams, joist.Identifier.ID);
                    if (supporting == null)
                    {
                        continue;
                    }

                    var key = $"{supporting.Identifier.ID}:{joist.Identifier.ID}:{Round3(end.X)}:{Round3(end.Y)}:{Round3(end.Z)}";
                    if (created.Contains(key))
                    {
                        continue;
                    }

                    if (CreateMezzFinPlateConnection(supporting, joist, settingsFile, end) != null)
                    {
                        created.Add(key);
                    }
                }
            }
        }

        private Beam FindSupportingBeam(Point joistEnd, List<Beam> candidates, int joistId)
        {
            foreach (var beam in candidates)
            {
                if (beam.Identifier.ID == joistId)
                {
                    continue;
                }

                if (IsPointOnBeamLine(joistEnd, beam, Tol))
                {
                    return beam;
                }
            }

            return null;
        }

        private static bool IsPointOnBeamLine(Point p, Beam beam, double tol)
        {
            var a = beam.StartPoint;
            var b = beam.EndPoint;

            var ab = new Vector(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            var ap = new Vector(p.X - a.X, p.Y - a.Y, p.Z - a.Z);
            var abLen2 = Dot(ab, ab);
            if (abLen2 < tol * tol)
            {
                return false;
            }

            var t = Dot(ap, ab) / abLen2;
            if (t < -1e-3 || t > 1.0 + 1e-3)
            {
                return false;
            }

            var proj = new Point(a.X + t * ab.X, a.Y + t * ab.Y, a.Z + t * ab.Z);
            return Distance(p, proj) <= tol;
        }

        private Connection CreateMezzFinPlateConnection(ModelObject primary, ModelObject secondary, string settingsFile, Point joistEnd)
        {
            var previousPlane = _model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            var primaryPart = primary as Part;
            if (primaryPart == null)
            {
                return null;
            }

            try
            {
                _model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TransformationPlane(primaryPart.GetCoordinateSystem()));

                var connection = new Connection
                {
                    Name = "Mezz_Finplate",
                    Number = -200000,
                    PositionType = PositionTypeEnum.MIDDLE_PLANE,
                    AutoDirectionType = AutoDirectionTypeEnum.AUTODIR_NA,
                    Class = 0,
                    Code = string.Empty
                };

                connection.SetPrimaryObject(primary);
                connection.SetSecondaryObject(secondary);

                if (!string.IsNullOrWhiteSpace(settingsFile))
                {
                    connection.LoadAttributesFromFile(settingsFile);
                }

                connection.SetAttribute("BoltDia", "16");
                connection.SetAttribute("ConnType", MapConnType(_data.Jconnectiontype));
                connection.SetAttribute("PlateRot", MapPlateRotation(_data.JConOrient, primaryPart, joistEnd));
                connection.SetAttribute("StiffDepth", MapStiffDepth(_data.JConStiffDep));

                // mm values (replacing generated imp() values)
                connection.SetAttribute("CPltThick", 8.0);
                connection.SetAttribute("EdgeDist", 30.0);
                connection.SetAttribute("EndDist", 40.0);
                connection.SetAttribute("FPltThick", 8.0);
                connection.SetAttribute("Gap", 10.0);
                connection.SetAttribute("Pitch", "80");
                connection.SetAttribute("PltLength", 200.0);
                connection.SetAttribute("PltPosition", "0");
                connection.SetAttribute("ReferX", 35.0);
                connection.SetAttribute("ReferY", 75.0);
                connection.SetAttribute("SymbolPosition", 2.5);

                connection.SetAttribute("IsBackStiff", "0");
                connection.SetAttribute("IsFullDep", "0");
                connection.SetAttribute("IsStandard", "0");

                return connection.Insert() ? connection : null;
            }
            finally
            {
                _model.GetWorkPlaneHandler().SetCurrentTransformationPlane(previousPlane);
            }
        }

        private static string MapConnType(int index)
        {
            switch (index)
            {
                case 0: return "MJC-J01";
                case 1: return "MJC-J02";
                case 2: return "MJC-J03";
                default: return "MJC-J02";
            }
        }

        private static string MapStiffDepth(int index)
        {
            switch (index)
            {
                case 0: return "FULL DEPTH";
                case 1: return "HALF DEPTH";
                case 2: return "NONE";
                default: return "NONE";
            }
        }

        private static string MapPlateRotation(int orient, Part primary, Point joistEnd)
        {
            var cs = primary.GetCoordinateSystem();
            var axisX = NormalizeOrDefault(cs.AxisX, new Vector(1, 0, 0));
            var beamOriginToEnd = new Vector(joistEnd.X - cs.Origin.X, joistEnd.Y - cs.Origin.Y, joistEnd.Z - cs.Origin.Z);
            var side = Dot(axisX, beamOriginToEnd);

            // Stable orientation: map UI + beam local side to left/right output.
            if (orient == 0) // LEFT TO JOIST
            {
                return side >= 0 ? "LEFT TO JOIST" : "RIGHT TO JOIST";
            }

            return side >= 0 ? "RIGHT TO JOIST" : "LEFT TO JOIST";
        }

        private List<Beam> InsertColumnsAtFourBeamNodes(
            CoordinateSystem cs,
            IReadOnlyList<double> xStations,
            IReadOnlyList<double> yStations,
            double mezzZ,
            string columnProfile)
        {
            var columns = new List<Beam>();

            for (var ix = 1; ix < xStations.Count - 1; ix++)
            {
                for (var iy = 1; iy < yStations.Count - 1; iy++)
                {
                    var x = xStations[ix];
                    var y = yStations[iy];
                    var basePt = LocalToGlobal(cs, x, y, 0);
                    var topPt = LocalToGlobal(cs, x, y, mezzZ);
                    var col = new Beam(Beam.BeamTypeEnum.COLUMN)
                    {
                        StartPoint = basePt,
                        EndPoint = topPt,
                        Name = "MEZZ_COLUMN"
                    };

                    col.Profile.ProfileString = string.IsNullOrWhiteSpace(columnProfile) ? "HSS200*200*8" : columnProfile;
                    col.Material.MaterialString = "S355";
                    col.Class = "4";
                    col.Position.Depth = Position.DepthEnum.MIDDLE;
                    col.Position.Plane = Position.PlaneEnum.MIDDLE;
                    col.Insert();
                    columns.Add(col);
                }
            }

            return columns;
        }

        private void CreateBeamToColumnConnections(List<Beam> xBeams, List<Beam> yBeams, List<Beam> columns)
        {
            foreach (var col in columns)
            {
                var top = col.EndPoint;
                var nearbyX = xBeams.Where(b => IsPointOnBeamLine(top, b, Tol)).ToList();
                var nearbyY = yBeams.Where(b => IsPointOnBeamLine(top, b, Tol)).ToList();

                foreach (var xb in nearbyX)
                {
                    CreateSimpleConnection(146, col, xb, "MEZZ_BC_WEB");
                }

                foreach (var yb in nearbyY)
                {
                    CreateSimpleConnection(141, col, yb, "MEZZ_BC_FLANGE");
                }
            }
        }

        private static void CreateSimpleConnection(int number, ModelObject primary, ModelObject secondary, string name)
        {
            var c = new Connection
            {
                Number = number,
                Name = name,
                PositionType = PositionTypeEnum.MIDDLE_PLANE,
                AutoDirectionType = AutoDirectionTypeEnum.AUTODIR_NA
            };

            c.SetPrimaryObject(primary);
            c.SetSecondaryObject(secondary);
            c.Insert();
        }

        private static Point LocalToGlobal(CoordinateSystem cs, double x, double y, double z)
        {
            var axisZ = Cross(cs.AxisX, cs.AxisY);
            return new Point(
                cs.Origin.X + x * cs.AxisX.X + y * cs.AxisY.X + z * axisZ.X,
                cs.Origin.Y + x * cs.AxisX.Y + y * cs.AxisY.Y + z * axisZ.Y,
                cs.Origin.Z + x * cs.AxisX.Z + y * cs.AxisY.Z + z * axisZ.Z);
        }

        private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static Vector Cross(Vector a, Vector b)
            => new Vector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static Vector NormalizeOrDefault(Vector v, Vector fallback)
        {
            var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (len < 1e-9)
            {
                return fallback;
            }

            return new Vector(v.X / len, v.Y / len, v.Z / len);
        }

        private static double Round3(double d) => Math.Round(d, 3);
    }
}
