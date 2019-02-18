namespace PSWordCloud

open System
open System.Collections
open System.Collections.Generic
open System.Linq
open System.Management.Automation
open System.Numerics
open System.Reflection
open System.Text.RegularExpressions
open System.Threading
open SkiaSharp

type internal WordOrientation =
    | Horizontal = 0
    | Vertical = 1
    | FlippedVertical = 2

module internal Utils =
    let FocusWordScale = 1.3f
    let BleedAreaScale = 1.15f
    let MinSaturation = 5.0f
    let MinBrightnessDifference = 25.0f
    let MaxPercentWidth = 0.75f
    let PaddingBaseScale = 0.05f
    let StrokeBaseScale = 0.02f

    let FontManager = SKFontManager.Default

    let FontList =
        FontManager.FontFamilies.OrderBy((fun x -> x), StringComparer.OrdinalIgnoreCase)

    let ColorLibrary =
        typeof<SKColor>.GetFields (BindingFlags.Static ||| BindingFlags.Public)
        |> Seq.map (fun field -> (field.Name, field.GetValue(null) :?> SKColor))
        |> Map.ofSeq

    let ColorNames = ColorLibrary |> Seq.map (fun item -> item.Key)

    let StandardColors = ColorLibrary |> Seq.map (fun item -> item.Value)

    let NamedColor color = ColorLibrary.[color]

    let StandardImageSizes =
        [
            ("480x800", ("Mobile Screen Size (small)", SKSizeI(480, 800)))
            ("640x1146", ("Mobile Screen Size (medium)", SKSizeI(640, 1146)))
            ("720p", ("Standard HD 1280x720", SKSizeI(1280, 720)))
            ("1080p", ("Full HD 1920x1080", SKSizeI(1920, 1080)))
            ("4K", ("Ultra HD 3840x2160", SKSizeI(3840, 2160)))
            ("A4", ("816x1056", SKSizeI(816, 1056) ))
            ("Poster11x17", ("1056x1632", SKSizeI(1056, 1632)))
            ("Poster18x24", ("1728x2304", SKSizeI(1728, 2304)))
            ("Poster24x36", ("2304x3456", SKSizeI(2304, 3456)))
        ] |> Map.ofSeq

    let ValueFrom (dict : IEnumerable) key =
        match dict with
        | :? IDictionary as d -> d.[key]
        | :? PSMemberInfoCollection<PSMemberInfo> as p -> p.[key].Value
        | _ -> raise (ArgumentTransformationMetadataException())

    let As<'T> value =
        try
            Some (LanguagePrimitives.ConvertTo<'T>(value))
        with
        | _ -> None

    let To<'T> = LanguagePrimitives.ConvertTo<'T>

    let lock (padlock : obj) task =
        Monitor.Enter padlock
        try
            task
        finally
            Monitor.Exit padlock

    let inline (|?) (a: 'a option) b = if a.IsSome then a.Value else b

open Utils
module internal Randomizer =
    let private _randomLock = obj()
    let mutable private _random : Random = null

    let private random() =
        if isNull _random then _random <- Random()
        _random

    let SetSeed seed =
        _random <- Random(seed)

    let NextSingle() =
        lock _randomLock <| random().NextDouble()
        |> single

    let NextInt() =
        lock _randomLock <| random().Next()

    let Shuffle<'T> (items : 'T list) =
        lock _randomLock <| List.permute (fun x -> random().Next(x)) items

module internal Extensions =
    type SKPoint with
        member self.Multiply factor = SKPoint(self.X * factor, self.Y * factor)

    type Single with
        member self.ToRadians() = self * (single Math.PI) / 180.0f

    type SKRect with
        member self.FallsOutside (region : SKRegion) =
            let bounds = SKRect(single region.Bounds.Left, single region.Bounds.Top, single region.Bounds.Right, single region.Bounds.Bottom)
            self.Top < bounds.Top
            || self.Bottom > bounds.Bottom
            || self.Left < bounds.Left
            || self.Right > bounds.Right

    type SKPaint with
        member self.NextWord color wordSize strokeWidth =
            self.TextSize <- wordSize
            self.IsStroke <- false
            self.Style <- SKPaintStyle.StrokeAndFill
            self.StrokeWidth <- wordSize * strokeWidth * Utils.StrokeBaseScale
            self.IsVerticalText <- false
            self.Color = color

        member self.DefaultWord = self.NextWord SKColors.Black

    type SKColor with
        member self.SortValue adjustmentValue =
            let (hue, saturation, brightness) = self.ToHsv()
            let vibranceAdjustment = brightness * (adjustmentValue * 0.5f) / (1.0f - saturation)
            brightness + vibranceAdjustment

    type SKRegion with
        member self.Op(path : SKPath, operation : SKRegionOperation) =
            use pathRegion = new SKRegion()
            pathRegion.SetPath(path) |> ignore
            self.Op(pathRegion, operation)

        member self.Intersects(rect : SKRect) =
            match self.Bounds.IsEmpty with
            | false ->
                use region = new SKRegion()
                region.SetRect(SKRectI.Round(rect)) |> ignore
                self.Intersects(region)
            | x -> x

        member self.Intersects(path : SKPath) =
            match self.Bounds.IsEmpty with
            | false ->
                use region = new SKRegion()
                region.SetPath(path) |> ignore
                self.Intersects(region)
            | x -> x

open Randomizer
open Extensions
module internal NewWordCloudCommandHelper =
    let StopWords = [
        "a";"about";"above";"after";"again";"against";"all";"am";"an";"and";"any";"are";"aren't";"as";"at";"be";
        "because";"been";"before";"being";"below";"between";"both";"but";"by";"can't";"cannot";"could";"couldn't";
        "did";"didn't";"do";"does";"doesn't";"doing";"don't";"down";"during";"each";"few";"for";"from";"further";
        "had";"hadn't";"has";"hasn't";"have";"haven't";"having";"he";"he'd";"he'll";"he's";"her";"here";"here's";
        "hers";"herself";"him";"himself";"his";"how";"how's";"i";"i'd";"i'll";"i'm";"i've";"if";"in";"into";"is";
        "isn't";"it";"it's";"its";"itself";"let's";"me";"more";"most";"mustn't";"my";"myself";"no";"nor";"not";"of";
        "off";"on";"once";"only";"or";"other";"ought";"our";"ours";"ourselves";"out";"over";"own";"same";"shan't";
        "she";"she'd";"she'll";"she's";"should";"shouldn't";"so";"some";"such";"than";"that";"that's";"the";"their";
        "theirs";"them";"themselves";"then";"there";"there's";"these";"they";"they'd";"they'll";"they're";"they've";
        "this";"those";"through";"to";"too";"under";"until";"up";"very";"was";"wasn't";"we";"we'd";"we'll";"we're";
        "we've";"were";"weren't";"what";"what's";"when";"when's";"where";"where's";"which";"while";"who";"who's";
        "whom";"why";"why's";"with";"won't";"would";"wouldn't";"you";"you'd";"you'll";"you're";"you've";"your";
        "yours";"yourself";"yourselves"
    ]

    let SplitChars = [
        ' ';'\n';'\t';'\r';'.';';';';';'\\';'/';'|';
        ':';'"';'?';'!';'{';'}';'[';']';':';'(';')';
        '<';'>';'“';'”';'*';'#';'%';'^';'&';'+';'='
    ]

    let ToRotationMatrix (point : SKPoint) orientation =
            match orientation with
            | WordOrientation.Vertical -> SKMatrix.MakeRotationDegrees(90.0f, point.X, point.Y)
            | WordOrientation.FlippedVertical -> SKMatrix.MakeRotationDegrees(-90.0f, point.X, point.Y)
            | WordOrientation.Horizontal -> SKMatrix.MakeIdentity()
            | _ -> raise (ArgumentException("Unknown orientation value."))

    let PrepareColorSet (set : SKColor list) (background : SKColor) (stroke : SKColor) max monochrome =
        let shuffledSet = Randomizer.Shuffle set
        let (_, _, bkgVal) = background.ToHsv()
        seq {
            for color in List.choose (fun color ->
                if color <> stroke && color <> background then
                    Some color
                else None) shuffledSet do
                    let (_, s, v) = color.ToHsv()
                    if not monochrome then
                        if s >= MinSaturation
                            && Math.Abs(v - bkgVal : single) > MinBrightnessDifference then
                            yield color
                    else
                        let level = byte (Math.Floor 255.0 * (float v) / 100.0)
                        yield SKColor(level, level, level)
        }

    let CountWords (wordList : seq<string>) (wordCounts : IDictionary<string, single>) =
        for word in wordList do
            let trimmedWord = Regex.Replace(word, "s$", String.Empty, RegexOptions.IgnoreCase)
            let pluralWord = String.Format("{0}s", word)
            if wordCounts.ContainsKey(trimmedWord) then
                wordCounts.[trimmedWord] <- wordCounts.[trimmedWord] + 1.0f
            else if wordCounts.ContainsKey(pluralWord) then
                wordCounts.[word] <- wordCounts.[pluralWord] + 1.0f
                wordCounts.Remove pluralWord |> ignore
            else
                wordCounts.[word] <- if wordCounts.ContainsKey(word) then wordCounts.[word] + 1.0f else 1.0f

    let AdjustFontScale (space : SKRect) averageWordFrequency wordCount =
        (space.Height + space.Width) / (8.0f * averageWordFrequency * single wordCount)

    let AdjustWordSize baseSize globalScale (scaleDictionary : IDictionary<string, single>) =
        baseSize * globalScale * (2.0f * NextSingle() / (1.0f + scaleDictionary.Values.Max() - scaleDictionary.Values.Min()) + 0.9f)

    let SortWordList (dictionary : IDictionary<string, single>) maxWords =
            dictionary.Keys.OrderByDescending(fun word -> dictionary.[word])
            |> Seq.take(if maxWords = 0 then Int32.MaxValue else maxWords)

    let GetRadiusIncrement wordSize distanceStep maxRadius padding percentComplete =
        (5.0f + NextSingle() * (2.5f + percentComplete / 10.0f)) * distanceStep * wordSize * (1.0f + padding) / maxRadius

    let GetRadialPoints (centre : SKPoint) radius radialStep aspectRatio =
        if radius = 0.0f then
            seq { yield centre }
        else
            let mutable point : Complex = Complex()
            let mutable angle =
                match NextSingle() with
                | x when x > 0.75f -> 0.0f
                | x when x > 0.5f -> 90.0f
                | x when x > 0.25f -> 180.0f
                | _ -> 270.0f

            let clockwise = NextSingle() > 0.5f
            let maxAngle = if clockwise then angle + 360.0f else angle - 360.0f
            let angleIncrement = radialStep * 360.0f / (15.0f * (radius / 6.0f + 1.0f)) * (if clockwise then 1.0f else -1.0f)

            seq {
                while (if clockwise then angle <= maxAngle else angle >= maxAngle) do
                    point <- Complex.FromPolarCoordinates(float radius, angle.ToRadians() |> float)
                    yield SKPoint(centre.X + single point.Real * aspectRatio, centre.Y + single point.Imaginary)

                    angle <- angle + angleIncrement
            }