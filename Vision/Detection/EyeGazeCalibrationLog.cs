﻿using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vision.Cv;

namespace Vision.Detection
{
    public class EyeGazeCalibrationLog
    {
        public int Version { get; set; } = 1;
        public FileNode File { get; set; }
        public Dictionary<Point3D, CalibratingPushData> Data { get; set; }

        public EyeGazeCalibrationLog(Dictionary<Point3D, CalibratingPushData> arg) : this()
        {
            Data = arg;
        }

        public EyeGazeCalibrationLog() : this(Storage.Root.GetFile($"CalibData-[{DateTime.Now}].clb"))
        {

        }

        public EyeGazeCalibrationLog(FileNode file)
        {
            Storage.FixPathChars(file);

            File = file;

            if (File.IsExist)
                Load();
        }

        public Mat Plot(ScreenProperties screen)
        {
            var errorList = new List<double>();
            var errorMM = new List<double>();
            var ptList = new List<Point>();
            var ptDistList = new List<Point>();

            var frameMargin = new Point(12, 12);
            var frameSize = new Size(480, 480);
            var frameBackSize = frameSize.Clone();
            frameBackSize.Width += frameMargin.Y * 2;
            frameBackSize.Height += frameMargin.Y * 2;

            var frame = MatTool.New(frameBackSize, MatType.CV_8UC3);
            using (Mat m = MatTool.New(frameSize, MatType.CV_8UC3))
            {
                m.DrawRectangle(new Rect(0, 0, m.Width, m.Height), Scalar.BgrWhite, -1);

                foreach (var item in Data)
                {
                    var pt = frameSize.Center;
                    var ptDist = pt.Clone();

                    var key3d = item.Key * (-1 / item.Key.Z);
                    var key = new Point(key3d.X, key3d.Y);
                    pt.X *= key.X;
                    pt.Y *= key.Y;

                    var gazeVec = item.Value.Face.GazeInfo.Vector;
                    ptDist.X *= gazeVec.X;
                    ptDist.Y *= gazeVec.Y;
                    pt += frameSize.Center;
                    ptDist += frameSize.Center;
                    ptList.Add(pt);
                    ptDistList.Add(ptDist);

                    var errorDiff = key - new Point(gazeVec.X, gazeVec.Y);
                    var error = Math.Sqrt(Math.Pow(errorDiff.X, 2) + Math.Pow(errorDiff.Y, 2));
                    errorList.Add(error);
                    errorMM.Add(Math.Abs(screen.Origin.Z - item.Value.Face.LandmarkTransform.Z / item.Value.Face.UnitPerMM) * error / 10);
                }

                var errorMax = errorList.Max();
                var errorMin = errorList.Min();
                var errorAvg = errorList.Average();
                var errorMMAvg = errorMM.Average();

                for (int i = 0; i < ptList.Count; i++)
                {
                    var pt = ptList[i];
                    var ptDist = ptDistList[i];
                    var error = errorList[i];
                    var alpha = (error - errorMin) / (errorMax - errorMin);
                    var color = Scalar.Blend(Scalar.BgrBlue, 1 - alpha, Scalar.BgrRed, alpha);
                    m.DrawArrow(pt, ptDist, color, 1, LineTypes.AntiAlias, 0.15);
                }

                Core.Cv.DrawText(m, 
                    $"Mean error: {errorAvg.ToString("0.000")}\n" +
                    $"Mean error(cm): {errorMMAvg.ToString("0.00")}\n" +
                    $"Mean error(degree): {(Math.Atan(errorAvg) / Math.PI * 180).ToString("0.00")}\n" +
                    $"Samples: {errorList.Count}",
                    new Point(10, 25), HersheyFonts.HersheyComplexSmall, 0.5, Scalar.BgrBlack, 1);
                
                frame.DrawRectangle(new Rect(0, 0, frame.Width, frame.Height), new Scalar(64, 64, 64), -1);
                Core.Cv.DrawMatAlpha(frame, m, frameMargin);
            }

            return frame;
        }

        public void Load()
        {
            using(Stream stream = File.Open())
            {
                using(StreamReader reader = new StreamReader(stream))
                {
                    var lineCount = 1;
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        try
                        {
                            LoadLine(line);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(this, $"Error on {File}:{lineCount}");
                            Logger.Error(this, ex);
                        }
                        lineCount++;
                    }
                }
            }
        }

        private void LoadLine(string line)
        {
            var trim = line.TrimStart();
            if (!IsComment(trim))
            {
                var spl = trim.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if(spl.Length == 2)
                {
                    var command = spl[0].ToLower();
                    var content = spl[1];
                    switch (command)
                    {
                        case "version":
                            Version = Convert.ToInt32(spl[1]);
                            Logger.Log(this, $"version = {Version}");
                            break;
                        case "log":
                            LoadLog(content);
                            break;
                        default:
                            Logger.Error(this, $"Unknown command {command}");
                            break;
                    }
                }
            }
        }

        private void LoadLog(string content)
        {
            var spl = content.Split('|');
            var splKey = spl[0].Split(',');
            var key = new Point3D(ToDoubleArray(splKey));
            var splData = spl[1].Split(',');
            throw new NotImplementedException("WIP");
        }

        private T[] Indexing<T>(T[] arr, int start, int end)
        {
            T[] ret = new T[end - start];
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = arr[start + i];
            }
            return ret;
        }

        private double[] ToDoubleArray(string[] spl)
        {
            double[] ret = new double[spl.Length];
            for (int i = 0; i < spl.Length; i++)
            {
                ret[i] = Convert.ToDouble(spl[i].Trim());
            }
            return ret;
        }

        private bool IsComment(string line)
        {
            if(line.Length > 1)
                return line[0] == '/' && line[1] == '/';
            return false;
        }

        public void Save()
        {
            if (File.IsExist)
            {
                File.Delete();
            }
            File.Create();

            using (Stream stream = File.Open())
            {
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine($"version {Version}");
                    builder.AppendLine("// Eye gaze calibrated log data");
                    builder.AppendLine("// log {key.X},{key.Y},{key.Z}|{gaze.X},{gaze.Y},{gaze.Z},{transform.X},{transform.Y},{transform.Z},{rotation.X},{rotation.Y},{rotation.Z}");
                    var data = Data;
                    foreach (var item in data)
                    {
                        var key = item.Key;
                        var face = item.Value.Face;
                        var gaze = face.GazeInfo.Vector;
                        var pad = 22;
                        var line = $"log {key.X.ToString().PadLeft(pad)},{key.Y.ToString().PadLeft(pad)},{key.Z.ToString().PadLeft(pad)}" +
                            $"|{gaze.X.ToString().PadLeft(pad)},{gaze.Y.ToString().PadLeft(pad)},{gaze.Z.ToString().PadLeft(pad)}," +
                            $"{face.LandmarkTransform.X.ToString().PadLeft(pad)},{face.LandmarkTransform.Y.ToString().PadLeft(pad)},{face.LandmarkTransform.Z.ToString().PadLeft(pad)}," +
                            $"{face.LandmarkRotation.X.ToString().PadLeft(pad)},{face.LandmarkRotation.Y.ToString().PadLeft(pad)},{face.LandmarkRotation.Z.ToString().PadLeft(pad)}";
                        builder.AppendLine(line);
                    }
                    writer.Write(builder.ToString());
                }
            }
        }
    }
}