/*
 * This file is a C# port of the Dart path_parsing library.
 * Original Dart source: https://github.com/flutter/packages/tree/main/third_party/packages/path_parsing
 *
 * The original library is part of Flutter's package ecosystem and provides utilities
 * for parsing SVG path data. This C# implementation aims to replicate the functionality
 * as closely as possible.
 */

using System;
using System.Collections.Generic;
using System.Numerics;

// ReSharper disable once CheckNamespace
namespace Harara.PathParsing.Net
{
    public class SvgPathParser
    {
        public static void WriteSvgPathDataToPath(string? svg, IPathProxy path)
        {
            if (string.IsNullOrEmpty(svg))
            {
                return;
            }

            var parser = new SvgPathStringSource(svg);
            var normalizer = new SvgPathNormalizer();
            foreach (var seg in parser.ParseSegments())
            {
                normalizer.EmitSegment(seg, path);
            }
        }
    }

    public interface IPathProxy
    {
        public abstract void MoveTo(double x, double y);
        public abstract void LineTo(double x, double y);
        public abstract void CubicTo(double x1, double y1, double x2, double y2, double x3, double y3);
        public abstract void Close();
    }

    public readonly struct PathOffset : IEquatable<PathOffset>
    {
        public double Dx { get; }
        public double Dy { get; }

        public PathOffset(double dx, double dy)
        {
            Dx = dx;
            Dy = dy;
        }

        public static PathOffset Zero => new PathOffset(0.0, 0.0);

        public double Direction => Math.Atan2(Dy, Dx);

        public PathOffset Translate(double translateX, double translateY)
        {
            return new PathOffset(Dx + translateX, Dy + translateY);
        }

        public static PathOffset operator +(PathOffset a, PathOffset b)
        {
            return new PathOffset(a.Dx + b.Dx, a.Dy + b.Dy);
        }

        public static PathOffset operator -(PathOffset a, PathOffset b)
        {
            return new PathOffset(a.Dx - b.Dx, a.Dy - b.Dy);
        }

        public static PathOffset operator *(PathOffset a, double operand)
        {
            return new PathOffset(a.Dx * operand, a.Dy * operand);
        }

        public override string ToString()
        {
            return $"PathOffset{{{Dx},{Dy}}}";
        }

        public override bool Equals(object? obj)
        {
            return obj is PathOffset other && other.Dx.Equals(Dx) && other.Dy.Equals(Dy);
        }

        public override int GetHashCode()
        {
            return (((17 * 23) ^ Dx.GetHashCode()) * 23) ^ Dy.GetHashCode();
        }

        public bool Equals(PathOffset other)
        {
            return Dx.Equals(other.Dx) && Dy.Equals(other.Dy);
        }
    }

    internal class SvgPathStringSource
    {
        private readonly string _string;
        private SvgPathSegType _previousCommand;
        private int _idx;
        private readonly int _length;

        public SvgPathStringSource(string s)
        {
            _string = s ?? throw new ArgumentNullException(nameof(s));
            _previousCommand = SvgPathSegType.Unknown;
            _idx = 0;
            _length = s.Length;
            SkipOptionalSvgSpaces();
        }

        private bool IsHtmlSpace(int character)
        {
            return character <= 32 && (character == 32 || character == 10 || character == 9 || character == 13 || character == 12);
        }

        private int SkipOptionalSvgSpaces()
        {
            while (true)
            {
                if (_idx >= _length)
                {
                    return -1;
                }

                int c = _string[_idx];
                if (!IsHtmlSpace(c))
                {
                    return c;
                }

                _idx++;
            }
        }

        private void SkipOptionalSvgSpacesOrDelimiter(int delimiter = ',')
        {
            int c = SkipOptionalSvgSpaces();
            if (c == delimiter)
            {
                _idx++;
                SkipOptionalSvgSpaces();
            }
        }

        private static bool IsNumberStart(int lookahead)
        {
            return (lookahead >= '0' && lookahead <= '9') || lookahead == '+' || lookahead == '-' || lookahead == '.';
        }

        private SvgPathSegType MaybeImplicitCommand(int lookahead, SvgPathSegType nextCommand)
        {
            if (!IsNumberStart(lookahead) || _previousCommand == SvgPathSegType.Close)
            {
                return nextCommand;
            }

            if (_previousCommand == SvgPathSegType.MoveToAbs)
            {
                return SvgPathSegType.LineToAbs;
            }

            if (_previousCommand == SvgPathSegType.MoveToRel)
            {
                return SvgPathSegType.LineToRel;
            }

            return _previousCommand;
        }

        private bool IsValidRange(double x)
        {
            return x >= double.MinValue && x <= double.MaxValue;
        }

        private bool IsValidExponent(double x)
        {
            return x >= -37 && x <= 38;
        }

        private int ReadCodeUnit()
        {
            if (_idx >= _length)
            {
                return -1;
            }
            return _string[_idx++];
        }

        private double ParseNumber()
        {
            SkipOptionalSvgSpaces();

            int sign = 1;
            int c = ReadCodeUnit();
            if (c == '+')
            {
                c = ReadCodeUnit();
            }
            else if (c == '-')
            {
                sign = -1;
                c = ReadCodeUnit();
            }

            if ((c < '0' || c > '9') && c != '.')
            {
                throw new InvalidOperationException("First character of a number must be one of [0-9+-.].");
            }

            double integer = 0.0;
            while ('0' <= c && c <= '9')
            {
                integer = integer * 10 + (c - '0');
                c = ReadCodeUnit();
            }

            if (!IsValidRange(integer))
            {
                throw new InvalidOperationException("Numeric overflow");
            }

            double decimalPart = 0.0;
            if (c == '.')
            {
                c = ReadCodeUnit();

                if (c < '0' || c > '9')
                {
                    throw new InvalidOperationException("There must be at least one digit following the .");
                }

                double frac = 1.0;
                while ('0' <= c && c <= '9')
                {
                    frac *= 0.1;
                    decimalPart += (c - '0') * frac;
                    c = ReadCodeUnit();
                }
            }

            double number = integer + decimalPart;
            number *= sign;

            if (_idx < _length && (c == 'e' || c == 'E') && (_string[_idx] != 'x' && _string[_idx] != 'm'))
            {
                c = ReadCodeUnit();

                bool exponentIsNegative = false;
                if (c == '+')
                {
                    c = ReadCodeUnit();
                }
                else if (c == '-')
                {
                    c = ReadCodeUnit();
                    exponentIsNegative = true;
                }

                if (c < '0' || c > '9')
                {
                    throw new InvalidOperationException("Missing exponent");
                }

                double exponent = 0.0;
                while (c >= '0' && c <= '9')
                {
                    exponent *= 10.0;
                    exponent += c - '0';
                    c = ReadCodeUnit();
                }
                if (exponentIsNegative)
                {
                    exponent = -exponent;
                }
                if (!IsValidExponent(exponent))
                {
                    throw new InvalidOperationException($"Invalid exponent {exponent}");
                }
                if (exponent != 0)
                {
                    number *= Math.Pow(10.0, exponent);
                }
            }

            if (!IsValidRange(number))
            {
                throw new InvalidOperationException("Numeric overflow");
            }

            if (c != -1)
            {
                --_idx;
                SkipOptionalSvgSpacesOrDelimiter();
            }
            return number;
        }

        private bool ParseArcFlag()
        {
            if (!HasMoreData)
            {
                throw new InvalidOperationException("Expected more data");
            }
            int flagChar = _string[_idx++];
            SkipOptionalSvgSpacesOrDelimiter();

            if (flagChar == '0')
            {
                return false;
            }
            else if (flagChar == '1')
            {
                return true;
            }
            else
            {
                throw new InvalidOperationException("Invalid flag value");
            }
        }

        public bool HasMoreData => _idx < _length;

        public IEnumerable<PathSegmentData> ParseSegments()
        {
            while (HasMoreData)
            {
                yield return ParseSegment();
            }
        }

        public PathSegmentData ParseSegment()
        {
            if (!HasMoreData)
            {
                throw new InvalidOperationException("No more data");
            }

            var segment = new PathSegmentData();
            int lookahead = _string[_idx];
            SvgPathSegType command = AsciiConstants.MapLetterToSegmentType(lookahead);

            if (_previousCommand == SvgPathSegType.Unknown)
            {
                if (command != SvgPathSegType.MoveToRel && command != SvgPathSegType.MoveToAbs)
                {
                    throw new InvalidOperationException("Expected to find moveTo command");
                }
                _idx++;
            }
            else if (command == SvgPathSegType.Unknown)
            {
                command = MaybeImplicitCommand(lookahead, command);
                if (command == SvgPathSegType.Unknown)
                {
                    throw new InvalidOperationException("Expected a path command");
                }
            }
            else
            {
                _idx++;
            }

            segment.Command = _previousCommand = command;

            switch (segment.Command)
            {
                case SvgPathSegType.CubicToRel:
                case SvgPathSegType.CubicToAbs:
                    segment.Point1 = new PathOffset(ParseNumber(), ParseNumber());
                    goto case SvgPathSegType.SmoothCubicToRel;
                case SvgPathSegType.SmoothCubicToRel:
                case SvgPathSegType.SmoothCubicToAbs:
                    segment.Point2 = new PathOffset(ParseNumber(), ParseNumber());
                    goto case SvgPathSegType.SmoothQuadToRel;
                case SvgPathSegType.MoveToRel:
                case SvgPathSegType.MoveToAbs:
                case SvgPathSegType.LineToRel:
                case SvgPathSegType.LineToAbs:
                case SvgPathSegType.SmoothQuadToRel:
                case SvgPathSegType.SmoothQuadToAbs:
                    segment.TargetPoint = new PathOffset(ParseNumber(), ParseNumber());
                    break;
                case SvgPathSegType.LineToHorizontalRel:
                case SvgPathSegType.LineToHorizontalAbs:
                    segment.TargetPoint = new PathOffset(ParseNumber(), segment.TargetPoint.Dy);
                    break;
                case SvgPathSegType.LineToVerticalRel:
                case SvgPathSegType.LineToVerticalAbs:
                    segment.TargetPoint = new PathOffset(segment.TargetPoint.Dx, ParseNumber());
                    break;
                case SvgPathSegType.Close:
                    SkipOptionalSvgSpaces();
                    break;
                case SvgPathSegType.QuadToRel:
                case SvgPathSegType.QuadToAbs:
                    segment.Point1 = new PathOffset(ParseNumber(), ParseNumber());
                    segment.TargetPoint = new PathOffset(ParseNumber(), ParseNumber());
                    break;
                case SvgPathSegType.ArcToRel:
                case SvgPathSegType.ArcToAbs:
                    segment.Point1 = new PathOffset(ParseNumber(), ParseNumber());
                    segment.ArcAngle = ParseNumber();
                    segment.ArcLarge = ParseArcFlag();
                    segment.ArcSweep = ParseArcFlag();
                    segment.TargetPoint = new PathOffset(ParseNumber(), ParseNumber());
                    break;
                case SvgPathSegType.Unknown:
                    throw new InvalidOperationException("Unknown segment command");
            }

            return segment;
        }
    }

    public static class AsciiConstants
    {
        /// `\t` (horizontal tab).
        public const int SlashT = 9;

        /// `\n` (newline).
        public const int SlashN = 10;

        /// `\f` (form feed).
        public const int SlashF = 12;

        /// `\r` (carriage return).
        public const int SlashR = 13;

        /// ` ` (space).
        public const int Space = 32;

        /// `+` (plus).
        public const int Plus = 43;

        /// `,` (comma).
        public const int Comma = 44;

        /// `-` (minus).
        public const int Minus = 45;

        /// `.` (period).
        public const int Period = 46;

        /// 0 (the number zero).
        public const int Number0 = 48;

        /// 1 (the number one).
        public const int Number1 = 49;

        /// 2 (the number two).
        public const int Number2 = 50;

        /// 3 (the number three).
        public const int Number3 = 51;

        /// 4 (the number four).
        public const int Number4 = 52;

        /// 5 (the number five).
        public const int Number5 = 53;

        /// 6 (the number six).
        public const int Number6 = 54;

        /// 7 (the number seven).
        public const int Number7 = 55;

        /// 8 (the number eight).
        public const int Number8 = 56;

        /// 9 (the number nine).
        public const int Number9 = 57;

        /// A
        public const int UpperA = 65;

        /// C
        public const int UpperC = 67;

        /// E
        public const int UpperE = 69;

        /// H
        public const int UpperH = 72;

        /// L
        public const int UpperL = 76;

        /// M
        public const int UpperM = 77;

        /// Q
        public const int UpperQ = 81;

        /// S
        public const int UpperS = 83;

        /// T
        public const int UpperT = 84;

        /// V
        public const int UpperV = 86;

        /// Z
        public const int UpperZ = 90;

        /// a
        public const int LowerA = 97;

        /// c
        public const int LowerC = 99;

        /// e
        public const int LowerE = 101;

        /// h
        public const int LowerH = 104;

        /// l
        public const int LowerL = 108;

        /// m
        public const int LowerM = 109;

        /// q
        public const int LowerQ = 113;

        /// s
        public const int LowerS = 115;

        /// t
        public const int LowerT = 116;

        /// v
        public const int LowerV = 118;

        /// x
        public const int LowerX = 120;

        /// z
        public const int LowerZ = 122;

        /// `~` (tilde)
        public const int Tilde = 126;


        internal static SvgPathSegType MapLetterToSegmentType(int c)
        {
            switch (c)
            {
                case 'M': return SvgPathSegType.MoveToAbs;
                case 'm': return SvgPathSegType.MoveToRel;
                case 'L': return SvgPathSegType.LineToAbs;
                case 'l': return SvgPathSegType.LineToRel;
                case 'H': return SvgPathSegType.LineToHorizontalAbs;
                case 'h': return SvgPathSegType.LineToHorizontalRel;
                case 'V': return SvgPathSegType.LineToVerticalAbs;
                case 'v': return SvgPathSegType.LineToVerticalRel;
                case 'C': return SvgPathSegType.CubicToAbs;
                case 'c': return SvgPathSegType.CubicToRel;
                case 'S': return SvgPathSegType.SmoothCubicToAbs;
                case 's': return SvgPathSegType.SmoothCubicToRel;
                case 'Q': return SvgPathSegType.QuadToAbs;
                case 'q': return SvgPathSegType.QuadToRel;
                case 'T': return SvgPathSegType.SmoothQuadToAbs;
                case 't': return SvgPathSegType.SmoothQuadToRel;
                case 'A': return SvgPathSegType.ArcToAbs;
                case 'a': return SvgPathSegType.ArcToRel;
                case 'Z': return SvgPathSegType.Close;
                case 'z': return SvgPathSegType.Close;
                default: return SvgPathSegType.Unknown;
            }
        }
    }

    internal enum SvgPathSegType
    {
        Unknown,
        MoveToAbs,
        MoveToRel,
        LineToAbs,
        LineToRel,
        LineToHorizontalAbs,
        LineToHorizontalRel,
        LineToVerticalAbs,
        LineToVerticalRel,
        CubicToAbs,
        CubicToRel,
        SmoothCubicToAbs,
        SmoothCubicToRel,
        QuadToAbs,
        QuadToRel,
        SmoothQuadToAbs,
        SmoothQuadToRel,
        ArcToAbs,
        ArcToRel,
        Close
    }

    internal class PathSegmentData
    {
        public SvgPathSegType Command { get; set; }
        public PathOffset TargetPoint { get; set; } = PathOffset.Zero;
        public PathOffset Point1 { get; set; } = PathOffset.Zero;
        public PathOffset Point2 { get; set; } = PathOffset.Zero;
        public bool ArcSweep { get; set; }
        public bool ArcLarge { get; set; }

        public double ArcAngle
        {
            get => Point2.Dx;
            set => Point2 = new PathOffset(value, Point2.Dy);
        }

        public double X => TargetPoint.Dx;
        public double Y => TargetPoint.Dy;
        public double X1 => Point1.Dx;
        public double Y1 => Point1.Dy;
        public double X2 => Point2.Dx;
        public double Y2 => Point2.Dy;

        public override string ToString()
        {
            return $"PathSegmentData{{{Command} {TargetPoint} {Point1} {Point2} {ArcSweep} {ArcLarge}}}";
        }
    }

    internal class SvgPathNormalizer
    {
        private PathOffset _currentPoint = PathOffset.Zero;
        private PathOffset _subPathPoint = PathOffset.Zero;
        private PathOffset _controlPoint = PathOffset.Zero;
        private SvgPathSegType _lastCommand = SvgPathSegType.Unknown;

        public void EmitSegment(PathSegmentData segment, IPathProxy path)
        {
            var normSeg = segment;
            switch (segment.Command)
            {
                case SvgPathSegType.QuadToRel:
                    normSeg.Point1 += _currentPoint;
                    normSeg.TargetPoint += _currentPoint;
                    break;
                case SvgPathSegType.CubicToRel:
                    normSeg.Point1 += _currentPoint;
                    goto case SvgPathSegType.SmoothCubicToRel;
                case SvgPathSegType.SmoothCubicToRel:
                    normSeg.Point2 += _currentPoint;
                    goto case SvgPathSegType.ArcToRel;
                case SvgPathSegType.MoveToRel:
                case SvgPathSegType.LineToRel:
                case SvgPathSegType.LineToHorizontalRel:
                case SvgPathSegType.LineToVerticalRel:
                case SvgPathSegType.SmoothQuadToRel:
                case SvgPathSegType.ArcToRel:
                    normSeg.TargetPoint += _currentPoint;
                    break;
                case SvgPathSegType.LineToHorizontalAbs:
                    normSeg.TargetPoint = new PathOffset(normSeg.TargetPoint.Dx, _currentPoint.Dy);
                    break;
                case SvgPathSegType.LineToVerticalAbs:
                    normSeg.TargetPoint = new PathOffset(_currentPoint.Dx, normSeg.TargetPoint.Dy);
                    break;
                case SvgPathSegType.Close:
                    normSeg.TargetPoint = _subPathPoint;
                    break;
            }

            switch (segment.Command)
            {
                case SvgPathSegType.MoveToRel:
                case SvgPathSegType.MoveToAbs:
                    _subPathPoint = normSeg.TargetPoint;
                    path.MoveTo(normSeg.TargetPoint.Dx, normSeg.TargetPoint.Dy);
                    break;
                case SvgPathSegType.LineToRel:
                case SvgPathSegType.LineToAbs:
                case SvgPathSegType.LineToHorizontalRel:
                case SvgPathSegType.LineToHorizontalAbs:
                case SvgPathSegType.LineToVerticalRel:
                case SvgPathSegType.LineToVerticalAbs:
                    path.LineTo(normSeg.TargetPoint.Dx, normSeg.TargetPoint.Dy);
                    break;
                case SvgPathSegType.Close:
                    path.Close();
                    break;
                case SvgPathSegType.SmoothCubicToRel:
                case SvgPathSegType.SmoothCubicToAbs:
                    if (!IsCubicCommand(_lastCommand))
                    {
                        normSeg.Point1 = _currentPoint;
                    }
                    else
                    {
                        normSeg.Point1 = ReflectedPoint(_currentPoint, _controlPoint);
                    }
                    goto case SvgPathSegType.CubicToAbs;
                case SvgPathSegType.CubicToRel:
                case SvgPathSegType.CubicToAbs:
                    _controlPoint = normSeg.Point2;
                    path.CubicTo(normSeg.Point1.Dx, normSeg.Point1.Dy, normSeg.Point2.Dx, normSeg.Point2.Dy, normSeg.TargetPoint.Dx, normSeg.TargetPoint.Dy);
                    break;
                case SvgPathSegType.SmoothQuadToRel:
                case SvgPathSegType.SmoothQuadToAbs:
                    if (!IsQuadraticCommand(_lastCommand))
                    {
                        normSeg.Point1 = _currentPoint;
                    }
                    else
                    {
                        normSeg.Point1 = ReflectedPoint(_currentPoint, _controlPoint);
                    }
                    goto case SvgPathSegType.QuadToAbs;
                case SvgPathSegType.QuadToRel:
                case SvgPathSegType.QuadToAbs:
                    _controlPoint = normSeg.Point1;
                    normSeg.Point1 = BlendPoints(_currentPoint, _controlPoint);
                    normSeg.Point2 = BlendPoints(normSeg.TargetPoint, _controlPoint);
                    path.CubicTo(normSeg.Point1.Dx, normSeg.Point1.Dy, normSeg.Point2.Dx, normSeg.Point2.Dy, normSeg.TargetPoint.Dx, normSeg.TargetPoint.Dy);
                    break;
                case SvgPathSegType.ArcToRel:
                case SvgPathSegType.ArcToAbs:
                    if (!DecomposeArcToCubic(_currentPoint, normSeg, path))
                    {
                        path.LineTo(normSeg.TargetPoint.Dx, normSeg.TargetPoint.Dy);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Invalid command type in path");
            }

            _currentPoint = normSeg.TargetPoint;

            if (!IsCubicCommand(segment.Command) && !IsQuadraticCommand(segment.Command))
            {
                _controlPoint = _currentPoint;
            }

            _lastCommand = segment.Command;
        }

        private bool IsCubicCommand(SvgPathSegType command)
        {
            return command == SvgPathSegType.CubicToAbs || command == SvgPathSegType.CubicToRel || command == SvgPathSegType.SmoothCubicToAbs || command == SvgPathSegType.SmoothCubicToRel;
        }

        private bool IsQuadraticCommand(SvgPathSegType command)
        {
            return command == SvgPathSegType.QuadToAbs || command == SvgPathSegType.QuadToRel || command == SvgPathSegType.SmoothQuadToAbs || command == SvgPathSegType.SmoothQuadToRel;
        }

        private PathOffset ReflectedPoint(PathOffset reflectedIn, PathOffset pointToReflect)
        {
            return new PathOffset(2 * reflectedIn.Dx - pointToReflect.Dx, 2 * reflectedIn.Dy - pointToReflect.Dy);
        }

        private const double OneOverThree = 1.0 / 3.0;

        private PathOffset BlendPoints(PathOffset p1, PathOffset p2)
        {
            return new PathOffset((p1.Dx + 2 * p2.Dx) * OneOverThree, (p1.Dy + 2 * p2.Dy) * OneOverThree);
        }

        private bool DecomposeArcToCubic(PathOffset currentPoint, PathSegmentData arcSegment, IPathProxy path)
        {
            double rx = Math.Abs(arcSegment.Point1.Dx);
            double ry = Math.Abs(arcSegment.Point1.Dy);
            if (rx == 0 || ry == 0)
            {
                return false;
            }

            if (arcSegment.TargetPoint.Equals(currentPoint))
            {
                return false;
            }

            double angle = Math.PI * arcSegment.ArcAngle / 180.0;

            PathOffset midPointDistance = (currentPoint - arcSegment.TargetPoint) * 0.5;

            var pointTransform = Matrix4x4.CreateRotationZ((float)-angle);

            PathOffset transformedMidPoint = MapPoint(pointTransform, new PathOffset(midPointDistance.Dx, midPointDistance.Dy));

            double squareRx = rx * rx;
            double squareRy = ry * ry;
            double squareX = transformedMidPoint.Dx * transformedMidPoint.Dx;
            double squareY = transformedMidPoint.Dy * transformedMidPoint.Dy;

            double radiiScale = squareX / squareRx + squareY / squareRy;
            if (radiiScale > 1.0)
            {
                rx *= Math.Sqrt(radiiScale);
                ry *= Math.Sqrt(radiiScale);
            }

            pointTransform = Matrix4x4.CreateRotationZ((float)-angle) * Matrix4x4.CreateScale((float)(1.0 / rx), (float)(1.0 / ry), (float)(1.0 / rx));
            
            var point1 = MapPoint(pointTransform, currentPoint);
            PathOffset point2 = MapPoint(pointTransform, arcSegment.TargetPoint);
            PathOffset delta = point2 - point1;

            double d = delta.Dx * delta.Dx + delta.Dy * delta.Dy;
            double scaleFactorSquared = Math.Max(1.0 / d - 0.25, 0.0);
            double scaleFactor = Math.Sqrt(scaleFactorSquared);
            if (!double.IsFinite(scaleFactor))
            {
                scaleFactor = 0.0;
            }

            if (arcSegment.ArcSweep == arcSegment.ArcLarge)
            {
                scaleFactor = -scaleFactor;
            }

            delta = delta * scaleFactor;
            PathOffset centerPoint = ((point1 + point2) * 0.5).Translate(-delta.Dy, delta.Dx);

            double theta1 = (point1 - centerPoint).Direction;
            double theta2 = (point2 - centerPoint).Direction;

            double thetaArc = theta2 - theta1;

            if (thetaArc < 0.0 && arcSegment.ArcSweep)
            {
                thetaArc += 2 * Math.PI;
            }
            else if (thetaArc > 0.0 && !arcSegment.ArcSweep)
            {
                thetaArc -= 2 * Math.PI;
            }

            pointTransform =  Matrix4x4.CreateScale((float)rx, (float)ry, (float)rx) * Matrix4x4.CreateRotationZ((float)angle);

            int segments = (int)Math.Ceiling(Math.Abs(thetaArc) / (Math.PI / 2 + 0.001));
            for (int i = 0; i < segments; ++i)
            {
                double startTheta = theta1 + i * thetaArc / segments;
                double endTheta = theta1 + (i + 1) * thetaArc / segments;

                double t = (8.0 / 6.0) * Math.Tan(0.25 * (endTheta - startTheta));
                if (!double.IsFinite(t))
                {
                    return false;
                }
                double sinStartTheta = Math.Sin(startTheta);
                double cosStartTheta = Math.Cos(startTheta);
                double sinEndTheta = Math.Sin(endTheta);
                double cosEndTheta = Math.Cos(endTheta);

                point1 = new PathOffset(cosStartTheta - t * sinStartTheta, sinStartTheta + t * cosStartTheta).Translate(centerPoint.Dx, centerPoint.Dy);
                PathOffset targetPoint = new PathOffset(cosEndTheta, sinEndTheta).Translate(centerPoint.Dx, centerPoint.Dy);
                point2 = targetPoint.Translate(t * sinEndTheta, -t * cosEndTheta);

                var cubicSegment = new PathSegmentData
                {
                    Command = SvgPathSegType.CubicToAbs,
                    Point1 = MapPoint(pointTransform, point1),
                    Point2 = MapPoint(pointTransform, point2),
                    TargetPoint = MapPoint(pointTransform, targetPoint)
                };

                path.CubicTo(cubicSegment.X1, cubicSegment.Y1, cubicSegment.X2, cubicSegment.Y2, cubicSegment.X, cubicSegment.Y);
            }
            return true;
        }

        private PathOffset MapPoint(Matrix4x4 transform, PathOffset point)
        {
            return new PathOffset(
                transform.M11 * point.Dx + transform.M21 * point.Dy + transform.M41,
                transform.M12 * point.Dx + transform.M22 * point.Dy + transform.M42
            );
        }
    }
}