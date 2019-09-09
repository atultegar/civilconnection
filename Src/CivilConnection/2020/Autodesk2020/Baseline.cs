﻿// Copyright (c) 2016 Autodesk, Inc. All rights reserved.
// Author: paolo.serra@autodesk.com
// 
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
// 
//  Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime;
using System.Runtime.InteropServices;

using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
using Autodesk.AECC.Interop.UiRoadway;
using Autodesk.AECC.Interop.Roadway;
using Autodesk.AECC.Interop.Land;
using System.Reflection;

using Autodesk.DesignScript.Runtime;
using Autodesk.DesignScript.Geometry;
using System.Xml;
using System.IO;

namespace CivilConnection
{

    /// <summary>
    /// Baseline obejct type.
    /// </summary>
    public class Baseline
    {
        #region PRIVATE PROPERTIES
        internal AeccBaseline _baseline;
        private double _start;
        private double _end;
        private double[] _stations;
        private int _index;
        private IList<BaselineRegion> _baselineRegions;
        #endregion

        #region PUBLIC PROPERTIES

        /// <summary>
        /// Gets the alignment.
        /// </summary>
        /// <value>
        /// The alignment.
        /// </value>
        public Alignment Alignment { get { return new Alignment(_baseline.Alignment); } }
        /// <summary>
        /// Gets the start station.
        /// </summary>
        /// <value>
        /// The start.
        /// </value>
        public double Start { get { return _start; } }
        /// <summary>
        /// Gets the end station.
        /// </summary>
        /// <value>
        /// The end.
        /// </value>
        public double End { get { return _end; } }
        /// <summary>
        /// Gets the stations.
        /// </summary>
        /// <value>
        /// The stations.
        /// </value>
        public double[] Stations { get { return _stations; } }
        /// <summary>
        /// Gets the geometry representation of the Baseline.
        /// </summary>
        /// <value>
        /// The poly curves.
        /// </value>
        public IList<PolyCurve> PolyCurves { get { return this.BaselinePolyCurves(); } }
        /// <summary>
        /// Gets the name of the Corridor.
        /// </summary>
        /// <value>
        /// The name of the Corridor.
        /// </value>
        public string CorridorName { get { return this._baseline.Corridor.DisplayName; } }
        /// <summary>
        /// Gets the internal element.
        /// </summary>
        /// <value>
        /// The internal element.
        /// </value>
        internal object InternalElement { get { return this._baseline; } }
        /// <summary>
        /// Gets the index of the Baseline in the Corridor
        /// </summary>
        /// <value>
        /// The index.
        /// </value>
        public int Index { get { return this._index; } }
        /// <summary>
        /// Gets the list of BaselineRegions
        /// </summary>
        internal IList<BaselineRegion> BaselineRegions { get { return this._baselineRegions; } }

        #endregion

        #region CONSTRUCTOR

        /// <summary>
        /// Internal constructor
        /// </summary>
        internal Baseline(AeccBaseline baseline, int index)
        {
            this._baseline = baseline;
            this._start = baseline.StartStation;
            this._end = baseline.EndStation;

            IList<double> stations = new List<double>();

            foreach (double s in baseline.GetSortedStations())
            {
                if (!stations.Contains(Math.Round(s, 3)))
                {
                    stations.Add(s);
                }
            }

            this._stations = stations.ToArray();
            this._index = index;

            // 20190524 - Start
            IList<BaselineRegion> output = new List<BaselineRegion>();

            int i = 0;

            foreach (AeccBaselineRegion blr in this._baseline.BaselineRegions)
            {
                // Can return Unspecified Error when the regions are not generated
                try
                {
                    output.Add(new BaselineRegion(this, blr, i));
                }
                catch (Exception ex)
                {
                    Utils.Log(string.Format("ERROR: Baseline Regions: {0}", ex.Message));
                    output.Add(null);
                }

                i += 1;
            }

            this._baselineRegions = output;
            // 20190524 - End
        }

        #endregion

        #region PRIVATE METHODS

        /// <summary>
        /// Private method to retrieve Baseline PolyCurves.
        /// </summary>
        /// <returns>A list of PolyCurves for each BaselineRegion.</returns>
        /// <remarks>In case of large dataset, the Geometry Working Range wiill return a warning. Set the Geometry Working Range to Medium.</remarks>
        private IList<PolyCurve> BaselinePolyCurves()
        {
            Utils.Log(string.Format("Baseline.BaselinePolyCurves started...", ""));

            IList<PolyCurve> polyCurves = new List<PolyCurve>();

            foreach (BaselineRegion blr in this.GetBaselineRegions())
            {
                IList<Point> baseLinePoints = new List<Point>();

                foreach (double station in blr.Stations)
                {
                    baseLinePoints.Add(this.PointByStationOffsetElevation(station, 0, 0));
                }

                polyCurves.Add(PolyCurve.ByPoints(baseLinePoints));
            }

            Utils.Log(string.Format("Baseline.BaselinePolyCurves completed.", ""));

            return polyCurves;
        }

        /// <summary>
        /// Returns the Offset Alignment names.
        /// </summary>
        /// <returns>The names of the offset Alignments, otherwise "None".</returns>
        private string OffsetAlignment()
        {
            if (null != this._baseline.MainBaselineFeatureLines.OffsetAlignment)
            {
                return this._baseline.MainBaselineFeatureLines.OffsetAlignment.DisplayName;
            }
            else
            {
                return "<None>";
            }
        }

        /// <summary>
        /// Returns a collection of Featurelines in the Baseline for the given code organized by regions.
        /// </summary>
        /// <param name="code">The code of the Featurelines.</param>
        /// <returns></returns>
        private IList<IList<Featureline>> GetFeaturelinesFromXML(string code)
        {
            Utils.Log(string.Format("Baseline.GetFeaturelinesFromXML started ({0})...", code));

            IList<IList<Featureline>> blFeaturelines = new List<IList<Featureline>>();
            PolyCurve pc = null;
            double side = 0;
            int ri = 0;

            string xmlPath = Path.Combine(Environment.GetEnvironmentVariable("TMP", EnvironmentVariableTarget.User), "CorridorFeatureLines.xml");  // Revit 2020 changed the path to the temp at a session level

            Utils.Log(xmlPath);

            this._baseline.Alignment.Document.SendCommand(string.Format("-ExportCorridorFeatureLinesToXml\n{0}\n", this._baseline.Corridor.Handle));

            DateTime start = DateTime.Now;

            while (true)
            {
                if (File.Exists(xmlPath))
                {
                    if (File.GetLastWriteTime(xmlPath) > start)
                    {
                        start = File.GetLastWriteTime(xmlPath);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            Utils.Log("XML acquired.");

            if (File.Exists(xmlPath))
            {
                IList<Featureline> output = new List<Featureline>();

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);

                foreach (XmlElement be in xmlDoc.GetElementsByTagName("Baseline")
                    .Cast<XmlElement>()
                    .Where(x => Convert.ToInt32(x.Attributes["Index"].Value) == this.Index && x.ParentNode.ParentNode.Attributes["Name"].Value == this.CorridorName))
                {
                    foreach (XmlElement fe in be.GetElementsByTagName("FeatureLine").Cast<XmlElement>().Where(x => x.Attributes["Code"].Value == code))
                    {
                        IList<Point> points = new List<Point>();

                        double isBreak = 0;

                        foreach (XmlElement p in fe.GetElementsByTagName("Point").Cast<XmlElement>().OrderBy(e => Convert.ToDouble(e.Attributes["Station"].Value)))
                        {
                            double x = 0;
                            double y = 0;
                            double z = 0;
                            double b = 0;

                            try
                            {
                                x = Convert.ToDouble(p.Attributes["X"].Value);
                            }
                            catch (Exception ex)
                            {
                                Utils.Log(string.Format("ERROR: {0} X {1}", Convert.ToDouble(p.Attributes["Station"].Value), ex.Message));
                            }

                            try
                            {
                                y = Convert.ToDouble(p.Attributes["Y"].Value);
                            }
                            catch (Exception ex)
                            {
                                Utils.Log(string.Format("ERROR: {0} Y {1}", Convert.ToDouble(p.Attributes["Station"].Value), ex.Message));
                            }

                            try
                            {
                                z = Convert.ToDouble(p.Attributes["Z"].Value);  // if Z is NaN because there is no profile associated in that station
                            }
                            catch (Exception ex)
                            {
                                Utils.Log(string.Format("ERROR: {0} Z {1}", Convert.ToDouble(p.Attributes["Station"].Value), ex.Message));
                            }

                            try
                            {
                                b = Convert.ToDouble(p.Attributes["IsBreak"].Value);
                            }
                            catch (Exception ex)
                            {
                                Utils.Log(string.Format("ERROR: {0} IsBreak {1}", Convert.ToDouble(p.Attributes["Station"].Value), ex.Message));
                            }

                            isBreak += b;

                            points.Add(Point.ByCoordinates(x, y, z));

                            if (isBreak == 1)
                            {
                                Utils.Log("Break Point.");

                                isBreak = 0;

                                points = Point.PruneDuplicates(points);

                                if (points.Count > 1)
                                {
                                    Utils.Log(string.Format("Points: {0}", points.Count));

                                    pc = PolyCurve.ByPoints(points);
                                    side = Convert.ToDouble(fe.Attributes["Side"].Value);
                                    ri = Convert.ToInt32(fe.Attributes["RegionIndex"].Value);

                                    output.Add(new Featureline(this, pc, code, side < 0 ? Featureline.SideType.Left : Featureline.SideType.Right, ri));
                                }

                                points = new List<Point>();
                            }
                        }

                        if (isBreak == 0 && points.Count > 0)
                        {
                            points = Point.PruneDuplicates(points);

                            if (points.Count > 1)
                            {
                                Utils.Log(string.Format("Points: {0}", points.Count));

                                pc = PolyCurve.ByPoints(points);
                                side = Convert.ToDouble(fe.Attributes["Side"].Value);
                                ri = Convert.ToInt32(fe.Attributes["RegionIndex"].Value);

                                output.Add(new Featureline(this, pc, code, side < 0 ? Featureline.SideType.Left : Featureline.SideType.Right, ri));
                            }
                        }
                    }
                }

                blFeaturelines = output.OrderBy(f => f.BaselineRegionIndex).GroupBy(f => f.BaselineRegionIndex).Cast<IList<Featureline>>().ToList();
            }
            else
            {
                Utils.Log("ERROR: Failed to locate CorridorFeatureLines.xml in the Temp folder.");
            }

            Utils.Log(string.Format("Baseline.GetFeaturelinesFromXML completed.", ""));

            return blFeaturelines;
        }

        #endregion

        #region PUBLIC METHODS

        /// <summary>
        /// Returns the list of BaselineRegions in the Baseline.
        /// </summary>
        /// <returns>A list of BaselineRegions.</returns>
        public IList<BaselineRegion> GetBaselineRegions()
        {
            //Utils.Log(string.Format("Baseline.GetBaselineRegions started...", ""));

            //IList<BaselineRegion> output = new List<BaselineRegion>();

            //int i = 0;

            //foreach (AeccBaselineRegion blr in this._baseline.BaselineRegions)
            //{
            //    // Can return Unspecified Error when the regions are not generated
            //    try
            //    {
            //        output.Add(new BaselineRegion(this, blr, i));
            //    }
            //    catch
            //    {
            //        output.Add(null);
            //    }

            //    i += 1;
            //}

            //Utils.Log(string.Format("Baseline.GetBaselineRegions completed.", ""));

            return this._baselineRegions;
        }

        /// <summary>
        /// Returns the index of the BaselineRegion in the Baseline that contains the station value.
        /// </summary>
        /// <param name="station">A double that represents a station along the corridor.</param>
        /// <returns>An integer for the BaselineRegion that contains the station value.</returns>
        /// <remarks>If the station input is less than the first station it returns 0. If the station input is greater than the last station it returns the number of BaselineRegions - 1.</remarks>
        public int GetBaselineRegionIndexByStation(double station)
        {
            Utils.Log(string.Format("Baseline.GetBaselineRegionIndexByStation started...", ""));

            int output = 0;

            foreach (AeccBaselineRegion region in this._baseline.BaselineRegions)
            {
                if (region.StartStation <= station && region.EndStation >= station)
                {
                    return output;
                }
                else
                {
                    output += 1;
                }
            }

            Utils.Log(string.Format("Baseline.GetBaselineRegionIndexByStation completed.", ""));

            return output;
        }

        /// <summary>
        /// Returns a point relative to the Baseline with station, offset and elevation.
        /// </summary>
        /// <param name="station">The distance measured along the Alignment.</param>
        /// <param name="offset">The horizontal displacement from the Baseline measured at a given station.</param>
        /// <param name="elevation">The vertical displacement from the Baseline measured at a given station.</param>
        /// <returns>A Dynamo Point.</returns>
        public Point PointByStationOffsetElevation(double station = 0, double offset = 0, double elevation = 0)
        {
            Utils.Log(string.Format("Baseline.PointByStationOffsetElevation started...", ""));

            Point p = null;

            var Xyz = this._baseline.StationOffsetElevationToXYZ(new double[] { station, offset, elevation });
            p = Point.ByCoordinates(Xyz[0], Xyz[1], Xyz[2]);

            Utils.Log(string.Format("Baseline.PointByStationOffsetElevation completed.", ""));

            return p;
        }

        /// <summary>
        /// Returns the Baseline CoordinateSystem at a station value.
        /// </summary>
        /// <param name="station">The input station.</param>
        /// <returns></returns>
        /// <remarks>if the station falls outside of the corridor it returns the Identity Coordinate System.</remarks>
        public CoordinateSystem CoordinateSystemByStation(double station = 0)
        {
            Utils.Log(string.Format("Baseline.CoordinateSystemByStation started...", ""));

            CoordinateSystem cs = null;

            if (station >= this.Start && station <= this.End)
            {
                var Xyz = this._baseline.GetDirectionAtStation(station);
                Vector y = Vector.ByCoordinates(Xyz[0], Xyz[1], Xyz[2]);
                Vector x = y.Cross(Vector.ZAxis());
                Point origin = this.PointByStationOffsetElevation(station, 0, 0);
                cs = CoordinateSystem.ByOriginVectors(origin, x, y, Vector.ZAxis());
            }
            else
            {
                var message = "The Station value is not compatible with the Baseline.";

                Utils.Log(string.Format("ERROR: {0}", message));

                // throw new Exception(message);
                return null;
            }

            if (cs == null)
            {
                Utils.Log(string.Format("ERROR: CoordinateSystem is null.", ""));
            }

            Utils.Log(string.Format("Baseline.CoordinateSystemByStation completed.", ""));

            return cs;
        }

        /// <summary>
        /// Returns the closest Baseline CoordinateSystem and uses the point as new origin.
        /// </summary>
        /// <param name="point">The input Point.</param>
        /// <returns>The CoordinateSystem.</returns>
        /// <remarks>if the station falls outside of the corridor it returns the Identity Coordinate System.</remarks>
        public CoordinateSystem GetCoordinateSystemByPoint(Point point)
        {
            Utils.Log(string.Format("Baseline.GetCoordinateSystemByPoint started...", ""));

            CoordinateSystem cs = CoordinateSystem.Identity();

            AeccAlignment alignment = this.Alignment.InternalElement as AeccAlignment;

            double station = 0;
            double offset = 0;

            alignment.StationOffset(point.X, point.Y, out station, out offset);

            cs = this.CoordinateSystemByStation(station);

            cs = CoordinateSystem.ByOriginVectors(point, cs.XAxis, cs.YAxis, cs.ZAxis);

            Utils.Log(string.Format("Baseline.GetCoordinateSystemByPoint completed.", ""));

            return cs;
        }

        /// <summary>
        /// Returns the station, offset, elevation of the point with respect to the Baseline.
        /// </summary>
        /// <param name="point">The input Point.</param>
        /// <returns>A double[].</returns>
        [MultiReturn(new string[] { "Station", "Offset", "Elevation" })]
        public Dictionary<string, object> GetStationOffsetElevationByPoint(Point point)
        {
            Utils.Log(string.Format("Baseline.GetStationOffsetElevationByPoint started...", ""));

            AeccAlignment alignment = this.Alignment.InternalElement as AeccAlignment;

            double station = 0;
            double offset = 0;

            alignment.StationOffset(point.X, point.Y, out station, out offset);

            double elevation = point.Z - PointByStationOffsetElevation(station, offset, 0).Z;

            Utils.Log(string.Format("Baseline.GetStationOffsetElevationByPoint completed.", ""));

            return new Dictionary<string, object> { { "Station", station }, { "Offset", offset }, { "Elevation", elevation } };
        }

        /// <summary>
        /// Gets the array station offset elevation by point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>A double[].</returns>
        [IsVisibleInDynamoLibrary(false)]
        public double[] GetArrayStationOffsetElevationByPoint(Point point)
        {
            Utils.Log(string.Format("Baseline.GetArrayStationOffsetElevationByPoint started...", ""));

            AeccAlignment alignment = this.Alignment.InternalElement as AeccAlignment;

            double station = 0;
            double offset = 0;

            alignment.StationOffset(point.X, point.Y, out station, out offset);

            //double elevation = point.Z - PointByStationOffsetElevation(station, offset, 0).Z;
            double elevation = point.Z - this._baseline.Profile.ElevationAt(station);

            Utils.Log(string.Format("Baseline.GetArrayStationOffsetElevationByPoint completed.", ""));

            return new double[] { station, offset, elevation };
        }

        /// <summary>
        /// Returns Offset Alignments from the Baseline.
        /// </summary>
        /// <returns>The offset Alignments. Null if there are any.</returns>
        public IList<Alignment> GetOffsetBaselinesAlignments()
        {
            Utils.Log(string.Format("Baseline.GetOffsetBaselinesAlignments started...", ""));

            if (null != this._baseline.OffsetBaselineFeatureLinesCol)
            {
                IList<Alignment> offsetAlignments = new List<Alignment>();

                foreach (AeccBaselineFeatureLines bfl in this._baseline.OffsetBaselineFeatureLinesCol)
                {
                    if (!bfl.IsMainBaseline)
                    {
                        Alignment offset = new Alignment(bfl.OffsetAlignment);
                        offsetAlignments.Add(offset);
                    }
                }

                Utils.Log(string.Format("Baseline.GetOffsetBaselinesAlignments completed.", ""));

                return offsetAlignments;
            }

            Utils.Log(string.Format("WARNING: Baseline.GetOffsetBaselinesAlignments returned null.", ""));

            return null;

        }

        /// <summary>
        /// Gets the featurelines by code and station
        /// </summary>
        /// <param name="code">the Featurelines code.</param>
        /// <param name="station">the station used to select the featurelines.</param>
        /// <returns></returns>
        public IList<Featureline> GetFeaturelinesByCodeStation(string code, double station)  // 1.1.0
        {
            Utils.Log(string.Format("Baseline.GetFeaturelinesByCodeStation({0}, {1}) Started...", code, station));

            IList<Featureline> blFeaturelines = new List<Featureline>();

            var b = this._baseline;

            // 20190122 -- Start

            AeccFeatureLines fs = null;

            try
            {
                fs = b.MainBaselineFeatureLines.FeatureLinesCol.Item(code);
            }
            catch (Exception ex)
            {
                Utils.Log(string.Format("ERROR 1: {0}", ex.Message));
            }

            if (fs != null)
            {
                AeccBaselineRegion reg = null;

                int regionIndex = 0;

                foreach (AeccBaselineRegion region in b.BaselineRegions)
                {
                    if (region.StartStation < station && region.EndStation > station || Math.Abs(station - region.StartStation) < 0.001 || Math.Abs(station - region.EndStation) < 0.001)
                    {
                        reg = region;
                        break;
                    }

                    ++regionIndex;
                }

                if (reg != null)
                {
                    foreach (var fl in fs.Cast<AeccFeatureLine>())
                    {
                        var pts = new List<Point>();

                        foreach (var pt in fl.FeatureLinePoints.Cast<AeccFeatureLinePoint>())
                        {
                            if (reg.StartStation < Math.Round(pt.Station, 5) && reg.EndStation > Math.Round(pt.Station, 5)
                                || Math.Abs(Math.Round(pt.Station, 5) - reg.StartStation) < 0.001
                                || Math.Abs(Math.Round(pt.Station, 5) - reg.EndStation) < 0.001)
                            {
                                var p = pt.XYZ;

                                //try
                                //{
                                //    pts.Add(Point.ByCoordinates(p[0], p[1], p[2]));
                                //}
                                //catch { }

                                try
                                {
                                    pts.Add(Point.ByCoordinates(p[0], p[1], p[2]));
                                }
                                catch (Exception ex)
                                {
                                    // 'System.Collections.Generic.IList<Autodesk.DesignScript.Geometry.Point>' does not contain a definition for 'Add'
                                    Utils.Log(string.Format("ERROR 2: {0}", ex.Message));
                                }
                            }
                        }

                        var points = Point.PruneDuplicates(pts);

                        if (points.Count() > 1)
                        {
                            PolyCurve pc = PolyCurve.ByPoints(points);

                            double offset = this.GetArrayStationOffsetElevationByPoint(pc.StartPoint)[1];  // 1.1.0

                            Featureline.SideType side = Featureline.SideType.Right;

                            if (offset < 0)
                            {
                                side = Featureline.SideType.Left;
                            }

                            blFeaturelines.Add(new Featureline(this, pc, code, side, regionIndex));

                            Utils.Log(string.Format("Featureline added", ""));
                        }

                        foreach (var pt in points)
                        {
                            pt.Dispose();
                        }
                    }
                }
            }

            // 20190122 -- End

            #region OLD CODE
            //int regionIndex = 0;

            //foreach (AeccBaselineRegion region in b.BaselineRegions)
            //{
            //    if (region.StartStation < station && region.EndStation > station || Math.Abs(station - region.StartStation) < 0.001 || Math.Abs(station - region.EndStation) < 0.001)  // 1.1.0
            //    {
            //        foreach (AeccFeatureLines coll in b.MainBaselineFeatureLines.FeatureLinesCol.Cast<AeccFeatureLines>().Where(fl => fl.FeatureLineCodeInfo.CodeName == code))  // 1.1.0
            //        {
            //            foreach (AeccFeatureLine f in coll.Cast<AeccFeatureLine>().Where(x => x.CodeName == code))  // maybe redundant?
            //            {
            //                double start = f.FeatureLinePoints.Item(0).Station;  // 1.1.0
            //                double end = f.FeatureLinePoints.Item(f.FeatureLinePoints.Count - 1).Station;  // 1.1.0

            //                bool reverse = start < end ? false : true;  // 1.1.0

            //                IList<Point> points = new List<Point>();

            //                foreach (AeccFeatureLinePoint p in f.FeatureLinePoints)
            //                {
            //                    Point point = Point.ByCoordinates(p.XYZ[0], p.XYZ[1], p.XYZ[2]);

            //                    double s = Math.Round(p.Station, 3);  // 1.1.0

            //                    if (s >= region.StartStation || Math.Abs(s - region.StartStation) < 0.001)
            //                    {
            //                        if (s <= region.EndStation || Math.Abs(s - region.EndStation) < 0.001)
            //                        {
            //                            points.Add(point);
            //                        }
            //                    }
            //                }

            //                points = Point.PruneDuplicates(points);

            //                if (points.Count > 1)
            //                {
            //                    PolyCurve pc = PolyCurve.ByPoints(points);

            //                    if (reverse)  // 1.1.0
            //                    {
            //                        pc = pc.Reverse() as PolyCurve;
            //                    }

            //                    double offset = this.GetArrayStationOffsetElevationByPoint(pc.StartPoint)[1];  // 1.1.0

            //                    Featureline.SideType side = Featureline.SideType.Right;

            //                    if (offset < 0)
            //                    {
            //                        side = Featureline.SideType.Left;
            //                    }

            //                    blFeaturelines.Add(new Featureline(this, pc, f.CodeName, side, regionIndex));
            //                }
            //            }
            //        }
            //    }

            //    regionIndex++;
            //}
            #endregion

            Utils.Log(string.Format("Baseline.GetFeaturelinesByCodeStation() Completed.", code));

            return blFeaturelines;
        }

        /// <summary>
        /// Gets the featurelines by code
        /// </summary>
        /// <param name="code">the Featurelines code.</param>
        /// <returns></returns>
        private IList<IList<Featureline>> GetFeaturelinesByCode_(string code)  // 1.1.0
        {
            Utils.Log(string.Format("Baseline.GetFeaturelinesByCode({0}) Started...", code));

            IList<IList<Featureline>> blFeaturelines = new List<IList<Featureline>>();

            var b = this._baseline;

            // 20190121 -- Start

            AeccFeatureLines fs = null;

            try
            {
                fs = b.MainBaselineFeatureLines.FeatureLinesCol.Item(code);
            }
            catch (Exception ex)
            {
                Utils.Log(string.Format("ERROR: {0}", ex.Message));
            }

            Dictionary<int, Dictionary<double, Point>> cFLs = new Dictionary<int, Dictionary<double, Point>>();

            int i = 0;

            if (fs != null)
            {
                Utils.Log(string.Format("Featurelines in region: {0}", fs.Count));

                foreach (var fl in fs.Cast<AeccFeatureLine>())
                {
                    Dictionary<double, Point> points = new Dictionary<double, Point>();

                    foreach (var pt in fl.FeatureLinePoints.Cast<AeccFeatureLinePoint>())
                    {
                        var p = pt.XYZ;

                        try
                        {
                            points.Add(Math.Round(pt.Station, 5), Point.ByCoordinates(p[0], p[1], p[2]));
                        }
                        catch (Exception ex)
                        {
                            Utils.Log(string.Format("ERROR: {0}", ex.Message));
                        }
                    }

                    cFLs.Add(i, points);

                    ++i;
                }
            }


            if (cFLs.Count > 0)
            {
                int regionIndex = 0;

                foreach (AeccBaselineRegion region in b.BaselineRegions)
                {
                    Utils.Log(string.Format("Processing region {0}...", regionIndex));

                    IList<Featureline> regFeaturelines = new List<Featureline>();

                    foreach (var k in cFLs.Keys)
                    {
                        IList<Point> points = new List<Point>();

                        var pts = cFLs[k];

                        if (pts.Keys.Count == 0)
                        {
                            continue;
                        }

                        foreach (double s in region.GetSortedStations())
                        {
                            var st = Math.Round(s, 5);

                            Point p = null;

                            if (pts.TryGetValue(st, out p))
                            {
                                points.Add(p);
                            }
                        }

                        points = Point.PruneDuplicates(points);

                        if (points.Count > 1)
                        {

                            PolyCurve pc = PolyCurve.ByPoints(points);

                            double offset = this.GetArrayStationOffsetElevationByPoint(pc.StartPoint)[1];  // 1.1.0

                            Featureline.SideType side = Featureline.SideType.Right;

                            if (offset < 0)
                            {
                                side = Featureline.SideType.Left;
                            }

                            regFeaturelines.Add(new Featureline(this, pc, code, side, regionIndex));

                            Utils.Log(string.Format("Featureline added", ""));
                        }

                        foreach (var pt in points)
                        {
                            pt.Dispose();
                        }
                    }

                    blFeaturelines.Add(regFeaturelines);

                    Utils.Log(string.Format("Region {0} completed.", regionIndex));

                    regionIndex++;
                }
            }

            // 20190121 -- End

            #region OLDCODE

            //int regionIndex = 0;

            //foreach (AeccBaselineRegion region in b.BaselineRegions)
            //{
            //    IList<Featureline> regFeaturelines = new List<Featureline>();

            //    foreach (AeccFeatureLines coll in b.MainBaselineFeatureLines.FeatureLinesCol.Cast<AeccFeatureLines>().Where(fl => fl.FeatureLineCodeInfo.CodeName == code))  // 1.1.0
            //    {
            //        foreach (AeccFeatureLine f in coll.Cast<AeccFeatureLine>().Where(x => x.CodeName == code))  // maybe redundant?
            //        {
            //            double start = f.FeatureLinePoints.Item(0).Station;  // 1.1.0
            //            double end = f.FeatureLinePoints.Item(f.FeatureLinePoints.Count - 1).Station;  // 1.1.0

            //            bool reverse = start < end ? false : true;  // 1.1.0

            //            IList<Point> points = new List<Point>();

            //            foreach (AeccFeatureLinePoint p in f.FeatureLinePoints)
            //            {
            //                Point point = Point.ByCoordinates(p.XYZ[0], p.XYZ[1], p.XYZ[2]);

            //                double s = Math.Round(p.Station, 5);  // 1.1.0

            //                if (s >= region.StartStation || Math.Abs(s - region.StartStation) < 0.001)
            //                {
            //                    if (s <= region.EndStation || Math.Abs(s - region.EndStation) < 0.001)
            //                    {
            //                        points.Add(point);
            //                    }
            //                }
            //            }

            //            points = Point.PruneDuplicates(points);

            //            if (points.Count > 1)
            //            {
            //                PolyCurve pc = PolyCurve.ByPoints(points);

            //                if (reverse)  // 1.1.0
            //                {
            //                    pc = pc.Reverse() as PolyCurve;
            //                }

            //                double offset = this.GetArrayStationOffsetElevationByPoint(pc.StartPoint)[1];  // 1.1.0

            //                Featureline.SideType side = Featureline.SideType.Right;

            //                if (offset < 0)
            //                {
            //                    side = Featureline.SideType.Left;
            //                }

            //                regFeaturelines.Add(new Featureline(this, pc, f.CodeName, side, regionIndex));
            //            }
            //        }
            //    }

            //    blFeaturelines.Add(regFeaturelines);

            //    regionIndex++;
            //}
            #endregion

            Utils.Log(string.Format("Baseline.GetFeaturelinesByCode() Completed.", code));

            return blFeaturelines;
        }

        /// <summary>
        /// Gets the featurelines by code
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public IList<IList<Featureline>> GetFeaturelinesByCode(string code)
        {
            return this.GetFeaturelinesFromXML(code);
        }

        /// <summary>
        /// Public textual representation of the Dynamo node preview
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return string.Format("Baseline(Start = {0}, End = {1})", Math.Round(this.Start, 2).ToString(), Math.Round(this.End, 2).ToString());
        }

        #endregion
    }
}
