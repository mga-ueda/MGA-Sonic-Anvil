using System.IO;
using System.Net.Http;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// Wave 単体モードの Music Playlist Container を WAAPI で作成する。
/// <para>
/// エクスポート WAV は分割せず常に 1 ファイル（元ファイル名を維持）。
/// 各 Music Segment のクリップは同一 WAV を参照し、Begin/End Trim Offset（WAAPI）と
/// 負の PlayAt（WAAPI 制約 [0, 1e10] のため WWU 直接編集）で区間を合わせる。
/// Play -E オン時は Playlist Container 既定ルール（Any to Any）の
/// PlaySourcePostExit も WWU 直接編集で立てる（WAAPI 非対応）。
/// </para>
/// </summary>
internal static class WaveOnlyWaapiImporter
{
    private const int FirstSegmentLookAheadMs = 50;
    private const int LookAheadMs = 500;
    private const int PrefetchLengthMs = 500;
    private const int CueTypeEntry = 0;
    private const int CueTypeExit = 1;

    private static readonly string[] ReturnFields = ["id", "name", "type", "path"];

    private readonly record struct MusicClipPlayAtFix(string ClipId, double PlayAtMs);

    public static async Task<string> ImportAsync(
        WaapiSettings settings,
        WaveOnlyPlan plan,
        AudioDocument document,
        string parentPath,
        string outputDirectory,
        bool playPostExit,
        CancellationToken cancellationToken = default)
    {
        var segments = plan.Segments;
        if (segments.Count == 0)
        {
            throw new InvalidOperationException(UiStrings.PreflightNoParts);
        }

        Directory.CreateDirectory(outputDirectory);

        // エクスポート WAV は分割禁止。全体を 1 ファイルで書く。
        var wavPath = Path.Combine(outputDirectory, ResolveExportFileName(plan, document));
        AudioCodec.SaveWaveRange(document, 0, document.FrameCount, wavPath);

        var timeout = TimeSpan.FromMilliseconds(Math.Max(settings.TimeoutMs, 30000));
        using var client = new WaapiHttpClient(settings.Url, timeout);
        var parent = parentPath.TrimEnd('\\');
        var containerPath = $"{parent}\\{plan.ContainerName}";

        await client.CallAsync(
                WaapiUris.CoreObjectSet,
                BuildPlaylistSetArgs(plan, parent, wavPath, document.SampleRate),
                new Dictionary<string, object> { ["return"] = ReturnFields },
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var segment in segments)
        {
            await ReplaceSegmentCuesAsync(
                    client,
                    $"{containerPath}\\{segment.Name}",
                    segment.EntryCueMs(document.SampleRate),
                    segment.ExitCueMs(document.SampleRate),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var playAtFixes = await ApplyClipTrimsAsync(
                client,
                segments,
                containerPath,
                wavPath,
                document.SampleRate,
                cancellationToken)
            .ConfigureAwait(false);

        if (playAtFixes.Count > 0 || playPostExit)
        {
            await ApplyWorkUnitPatchesAsync(
                    client,
                    containerPath,
                    plan.ContainerName,
                    playAtFixes,
                    playPostExit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await WaapiSelection.TrySelectAsync(settings, containerPath, cancellationToken).ConfigureAwait(false);
        return containerPath;
    }

    /// <summary>元ファイル名を維持した WAV 名（拡張子は .wav に統一）。</summary>
    private static string ResolveExportFileName(WaveOnlyPlan plan, AudioDocument document)
    {
        var stem = Path.GetFileNameWithoutExtension(document.SourcePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = plan.ContainerName;
        }

        return WaveOnlyPlanBuilder.SanitizeWwiseName(stem) + ".wav";
    }

    private static Dictionary<string, object?> BuildPlaylistSetArgs(
        WaveOnlyPlan plan,
        string parentPath,
        string wavPath,
        int sampleRate)
    {
        var segments = plan.Segments;
        var segmentDefs = new List<object>(segments.Count);
        var itemDefs = new List<object>(segments.Count);
        var playlistPath = $"{parentPath}\\{plan.ContainerName}";
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            segmentDefs.Add(BuildSegmentDef(segment, wavPath, sampleRate, isFirst: i == 0));
            itemDefs.Add(new Dictionary<string, object?>
            {
                ["type"] = "MusicPlaylistItem",
                ["name"] = string.Empty,
                ["@PlaylistItemType"] = 1,
                ["@LoopCount"] = segment.LoopInfinite ? 0 : 1,
                ["@Segment"] = $"{playlistPath}\\{segment.Name}",
            });
        }

        return new Dictionary<string, object?>
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = parentPath,
                    ["children"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "MusicPlaylistContainer",
                            ["name"] = plan.ContainerName,
                            ["children"] = segmentDefs,
                            ["@PlaylistRoot"] = new Dictionary<string, object?>
                            {
                                ["type"] = "MusicPlaylistItem",
                                ["name"] = string.Empty,
                                ["@PlaylistItemType"] = 0,
                                ["@PlayMode"] = 0,
                                ["@LoopCount"] = 1,
                                ["children"] = itemDefs,
                            },
                        },
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    private static Dictionary<string, object?> BuildSegmentDef(
        WaveOnlySegment segment,
        string wavPath,
        int sampleRate,
        bool isFirst)
    {
        var track = new Dictionary<string, object?>
        {
            ["type"] = "MusicTrack",
            ["name"] = segment.Name,
            ["@IsStreamingEnabled"] = true,
            ["@IsZeroLatency"] = isFirst,
            ["@LookAheadTime"] = isFirst ? FirstSegmentLookAheadMs : LookAheadMs,
            ["import"] = new Dictionary<string, object?>
            {
                ["files"] = new object[]
                {
                    new Dictionary<string, object?> { ["audioFile"] = wavPath },
                },
            },
        };
        if (isFirst)
        {
            track["@PreFetchLength"] = PrefetchLengthMs;
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "MusicSegment",
            ["name"] = segment.Name,
            ["@OverrideClockSettings"] = true,
            ["@Tempo"] = 120,
            ["@TimeSignatureUpper"] = 4,
            ["@TimeSignatureLower"] = 4,
            ["@EndPosition"] = segment.DurationMs(sampleRate),
            ["children"] = new object[] { track },
        };
    }

    private static Task ReplaceSegmentCuesAsync(
        WaapiHttpClient client,
        string segmentPath,
        double entryCueMs,
        double exitCueMs,
        CancellationToken cancellationToken) =>
        client.CallAsync(
            WaapiUris.CoreObjectSet,
            new Dictionary<string, object?>
            {
                ["objects"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["object"] = segmentPath,
                        ["@Cues"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["type"] = "MusicCue",
                                ["name"] = string.Empty,
                                ["@CueType"] = CueTypeEntry,
                                ["@TimeMs"] = entryCueMs,
                            },
                            new Dictionary<string, object?>
                            {
                                ["type"] = "MusicCue",
                                ["name"] = string.Empty,
                                ["@CueType"] = CueTypeExit,
                                ["@TimeMs"] = exitCueMs,
                            },
                        },
                    },
                },
                ["onNameConflict"] = "merge",
                ["listMode"] = "replaceAll",
            },
            cancellationToken: cancellationToken);

    // ------------------------------------------------------------------
    // MusicClip トリム（単一 WAV をセグメント区間へ合わせる）
    // ------------------------------------------------------------------

    /// <summary>
    /// 各セグメントのクリップに Begin/End Trim Offset（ソース内絶対ミリ秒）を設定する。
    /// トリム後の内容を 0 位置へ寄せる PlayAt（負値）は WAAPI で書けないため、
    /// 必要なパッチ一覧を返し WWU 直接編集へ回す。
    /// </summary>
    private static async Task<List<MusicClipPlayAtFix>> ApplyClipTrimsAsync(
        WaapiHttpClient client,
        IReadOnlyList<WaveOnlySegment> segments,
        string containerPath,
        string wavPath,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        var fixes = new List<MusicClipPlayAtFix>();
        var allClips = await QueryAllMusicClipsAsync(client, cancellationToken).ConfigureAwait(false);
        var wavStem = Path.GetFileNameWithoutExtension(wavPath);
        foreach (var segment in segments)
        {
            var beginMs = segment.StartFrame * 1000.0 / sampleRate;
            var endMs = segment.EndFrame * 1000.0 / sampleRate;
            var trackPath = $"{containerPath}\\{segment.Name}\\{segment.Name}";
            var clipIds = FindMusicClipsForTrack(allClips, trackPath, wavStem);
            if (clipIds.Count == 0)
            {
                throw new InvalidOperationException(UiStrings.ErrMusicClipNotFound(trackPath));
            }

            if (clipIds.Count > 1)
            {
                throw new InvalidOperationException(UiStrings.ErrMusicClipAmbiguous(trackPath, clipIds.Count));
            }

            var clipId = clipIds[0];
            await SetPropertyAsync(client, clipId, "BeginTrimOffset", beginMs, cancellationToken)
                .ConfigureAwait(false);
            await SetPropertyAsync(client, clipId, "EndTrimOffset", endMs, cancellationToken)
                .ConfigureAwait(false);
            if (beginMs > 0.0005)
            {
                fixes.Add(new MusicClipPlayAtFix(clipId, -beginMs));
            }
        }

        return fixes;
    }

    private static async Task<List<(string Id, string Path)>> QueryAllMusicClipsAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken)
    {
        var list = new List<(string Id, string Path)>();
        var result = await client.CallAsync(
                WaapiUris.CoreObjectGet,
                new Dictionary<string, object?> { ["waql"] = "$ from type MusicClip" },
                new Dictionary<string, object?> { ["return"] = ReturnFields },
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("return", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl))
            {
                continue;
            }

            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var path = item.TryGetProperty("path", out var pathEl)
                ? pathEl.GetString() ?? string.Empty
                : string.Empty;
            list.Add((id, path));
        }

        return list;
    }

    /// <summary>Track パス直下の MusicClip のみを対象にする（緩い Contains は誤爆する）。</summary>
    private static List<string> FindMusicClipsForTrack(
        IReadOnlyList<(string Id, string Path)> allClips,
        string trackPath,
        string wavStem)
    {
        var matches = new List<string>();
        var trackFull = trackPath.TrimEnd('\\');
        var prefix = trackFull + "\\";
        foreach (var (id, path) in allClips)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, trackFull, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        if (matches.Count > 0 || string.IsNullOrEmpty(wavStem))
        {
            return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var exactClipPath = prefix + wavStem;
        foreach (var (id, path) in allClips)
        {
            if (string.Equals(path, exactClipPath, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Task SetPropertyAsync(
        WaapiHttpClient client,
        string objectId,
        string property,
        object value,
        CancellationToken cancellationToken) =>
        client.CallAsync(
            WaapiUris.CoreObjectSetProperty,
            new Dictionary<string, object?>
            {
                ["object"] = objectId,
                ["property"] = property,
                ["value"] = value,
            },
            cancellationToken: cancellationToken);

    // ------------------------------------------------------------------
    // WWU 直接編集（負の PlayAt / Play post-exit）
    // 手順: project.save → 対象 WWU 特定 → project.close → XML パッチ → project.open
    // ------------------------------------------------------------------

    private static async Task ApplyWorkUnitPatchesAsync(
        WaapiHttpClient client,
        string containerPath,
        string containerName,
        IReadOnlyList<MusicClipPlayAtFix> playAtFixes,
        bool playPostExit,
        CancellationToken cancellationToken)
    {
        var wwuPath = await QuerySingleReturnStringAsync(
                client,
                $"$ \"{containerPath.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
                "filePath",
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(wwuPath) || !File.Exists(wwuPath))
        {
            throw new InvalidOperationException(UiStrings.ErrWorkUnitPathUnknown);
        }

        var projectPath = await QuerySingleReturnStringAsync(
                client,
                "$ from type Project",
                "filePath",
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
        {
            throw new InvalidOperationException(UiStrings.ErrProjectPathUnknown);
        }

        await client.CallAsync(WaapiUris.CoreProjectSave, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await client.CallAsync(
                    WaapiUris.UiProjectClose,
                    new Dictionary<string, object?> { ["bypassSave"] = true },
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientHttpError(ex))
        {
            // クローズ開始と同時に HTTP 接続が切れて応答を受け取れないことがある。
            // 実際にクローズされたかは直後の WaitForProjectClosedAsync で確認する。
        }

        await WaitForProjectClosedAsync(client).ConfigureAwait(false);

        try
        {
            PatchWorkUnitFile(wwuPath, containerName, playAtFixes, playPostExit);
        }
        finally
        {
            await CallWithLockRetryAsync(
                    client,
                    WaapiUris.UiProjectOpen,
                    new Dictionary<string, object?> { ["path"] = projectPath })
                .ConfigureAwait(false);
        }

        await WaitForProjectLoadedAsync(client, projectPath).ConfigureAwait(false);

        foreach (var fix in playAtFixes)
        {
            var actual = await QueryClipReal64Async(client, fix.ClipId, "@PlayAt").ConfigureAwait(false);
            if (actual is null || Math.Abs(actual.Value - fix.PlayAtMs) > 0.01)
            {
                throw new InvalidOperationException(
                    UiStrings.ErrPlayAtVerifyFailed(fix.ClipId, fix.PlayAtMs, actual));
            }
        }
    }

    private static void PatchWorkUnitFile(
        string wwuPath,
        string containerName,
        IReadOnlyList<MusicClipPlayAtFix> playAtFixes,
        bool playPostExit)
    {
        WaitForExclusiveFileAccess(wwuPath);

        var doc = new System.Xml.XmlDocument { PreserveWhitespace = true };
        doc.Load(wwuPath);

        foreach (var fix in playAtFixes)
        {
            var clipNode = doc.SelectSingleNode($"//MusicClip[@ID='{fix.ClipId}']")
                as System.Xml.XmlElement
                ?? throw new InvalidOperationException(
                    UiStrings.ErrPlayAtClipXmlMissing(fix.ClipId, wwuPath));

            var propertyList = clipNode.SelectSingleNode("PropertyList") as System.Xml.XmlElement;
            if (propertyList is null)
            {
                propertyList = doc.CreateElement("PropertyList");
                clipNode.PrependChild(propertyList);
            }

            UpsertReal64Property(doc, propertyList, "PlayAt", fix.PlayAtMs);
        }

        if (playPostExit)
        {
            var rule = FindPlaylistAnyToAnyRule(doc, containerName)
                ?? throw new InvalidOperationException(
                    UiStrings.ErrPlaylistAnyToAnyRuleMissing(containerName, wwuPath));
            var propertyList = EnsureChildElement(doc, rule, "PropertyList", prepend: true);
            // UI「Play -E」＝ WObjects の PlaySourcePostExit（@PlayPostExit は無効）。
            UpsertBoolProperty(doc, propertyList, "PlaySourcePostExit", value: true);
        }

        doc.Save(wwuPath);

        if (playPostExit)
        {
            VerifyPlaylistPostExitInWorkUnitFile(wwuPath, containerName, expected: true);
        }
    }

    private static void VerifyPlaylistPostExitInWorkUnitFile(
        string wwuPath,
        string containerName,
        bool expected)
    {
        var doc = new System.Xml.XmlDocument { PreserveWhitespace = true };
        doc.Load(wwuPath);
        var rule = FindPlaylistAnyToAnyRule(doc, containerName)
            ?? throw new InvalidOperationException(
                UiStrings.ErrPlaylistAnyToAnyRuleMissing(containerName, wwuPath));
        var prop = rule.SelectSingleNode("PropertyList/Property[@Name='PlaySourcePostExit']")
            as System.Xml.XmlElement;
        var actualText = prop?.GetAttribute("Value");
        var actual = string.Equals(actualText, "True", StringComparison.OrdinalIgnoreCase)
            || actualText == "1";
        if (actual != expected)
        {
            throw new InvalidOperationException(
                UiStrings.ErrPostExitVerifyFailed(containerName, expected, actual));
        }
    }

    /// <summary>
    /// Music Playlist Container の TransitionRoot 直下から既定の Any to Any ルールを探す。
    /// コンテナ作成時に Wwise が自動生成するルール（Source / Destination とも Any）が対象。
    /// </summary>
    private static System.Xml.XmlElement? FindPlaylistAnyToAnyRule(
        System.Xml.XmlDocument doc,
        string containerName)
    {
        var containers = doc.SelectNodes("//MusicPlaylistContainer");
        if (containers is null)
        {
            return null;
        }

        foreach (System.Xml.XmlNode node in containers)
        {
            if (node is not System.Xml.XmlElement container
                || !string.Equals(container.GetAttribute("Name"), containerName, StringComparison.Ordinal))
            {
                continue;
            }

            var rules = container.SelectNodes(
                "ReferenceList/Reference[@Name='TransitionRoot']/Custom/MusicTransition"
                + "/ChildrenList/MusicTransition");
            if (rules is null)
            {
                return null;
            }

            foreach (System.Xml.XmlNode ruleNode in rules)
            {
                if (ruleNode is not System.Xml.XmlElement rule || IsMusicTransitionFolder(rule))
                {
                    continue;
                }

                if (ReadTransitionContextType(rule, "SourceContextType") == 0
                    && ReadTransitionContextType(rule, "DestinationContextType") == 0)
                {
                    return rule;
                }
            }

            return null;
        }

        return null;
    }

    private static bool IsMusicTransitionFolder(System.Xml.XmlElement element)
    {
        var isFolder = element.SelectSingleNode("PropertyList/Property[@Name='IsFolder']")
            as System.Xml.XmlElement;
        return isFolder is not null
            && string.Equals(isFolder.GetAttribute("Value"), "True", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadTransitionContextType(System.Xml.XmlElement rule, string propertyName)
    {
        var property = rule.SelectSingleNode($"PropertyList/Property[@Name='{propertyName}']")
            as System.Xml.XmlElement;
        if (property is null)
        {
            return 0;
        }

        return int.TryParse(
            property.GetAttribute("Value"),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static System.Xml.XmlElement EnsureChildElement(
        System.Xml.XmlDocument doc,
        System.Xml.XmlElement parent,
        string name,
        bool prepend)
    {
        if (parent.SelectSingleNode(name) is System.Xml.XmlElement existing)
        {
            return existing;
        }

        var created = doc.CreateElement(name);
        if (prepend && parent.HasChildNodes)
        {
            parent.InsertBefore(created, parent.FirstChild);
        }
        else
        {
            parent.AppendChild(created);
        }

        return created;
    }

    private static void UpsertReal64Property(
        System.Xml.XmlDocument doc,
        System.Xml.XmlElement propertyList,
        string name,
        double value)
    {
        var text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (propertyList.SelectSingleNode($"Property[@Name='{name}']")
            is System.Xml.XmlElement existing)
        {
            existing.SetAttribute("Value", text);
            return;
        }

        var property = doc.CreateElement("Property");
        property.SetAttribute("Name", name);
        property.SetAttribute("Type", "Real64");
        property.SetAttribute("Value", text);
        propertyList.AppendChild(property);
    }

    private static void UpsertBoolProperty(
        System.Xml.XmlDocument doc,
        System.Xml.XmlElement propertyList,
        string name,
        bool value)
    {
        var text = value ? "True" : "False";
        if (propertyList.SelectSingleNode($"Property[@Name='{name}']")
            is System.Xml.XmlElement existing)
        {
            existing.SetAttribute("Type", "bool");
            existing.SetAttribute("Value", text);
            return;
        }

        var property = doc.CreateElement("Property");
        property.SetAttribute("Name", name);
        property.SetAttribute("Type", "bool");
        property.SetAttribute("Value", text);
        propertyList.AppendChild(property);
    }

    /// <summary>指定ファイルを排他モードで開けるまで待つ（最大 30 秒）。</summary>
    private static void WaitForExclusiveFileAccess(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
            }
        }
    }

    /// <summary>
    /// プロジェクトのクローズ／ロード中に WAAPI の HTTP 接続が一時的に切れたときの例外か。
    /// （HttpRequestException=接続断、TaskCanceledException=HttpClient タイムアウト）
    /// </summary>
    private static bool IsTransientHttpError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException;

    private static async Task WaitForProjectClosedAsync(WaapiHttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await client.CallAsync(
                        WaapiUris.CoreObjectGet,
                        new Dictionary<string, object?> { ["waql"] = "$ from type Project" },
                        new Dictionary<string, object?> { ["return"] = new[] { "id" } },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!result.TryGetProperty("return", out var arr)
                    || arr.ValueKind != JsonValueKind.Array
                    || arr.GetArrayLength() == 0)
                {
                    return;
                }
            }
            catch (WaapiException ex) when (
                ex.Message.Contains(WaapiUris.Locked, StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("exclusive lock", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("in progress", StringComparison.OrdinalIgnoreCase))
            {
                // クローズ進行中。待って再確認する。
            }
            catch (WaapiException)
            {
                // 「プロジェクトが読み込まれていない」等 → クローズ完了とみなす。
                return;
            }
            catch (Exception ex) when (IsTransientHttpError(ex))
            {
                // クローズ中は HTTP 接続自体が一瞬落ちることがある。待って再確認する。
            }

            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
        }

        throw new InvalidOperationException(UiStrings.ErrProjectCloseTimeout);
    }

    /// <summary>
    /// 再オープンしたプロジェクトのロード完了（クエリで .wproj パスが返る状態）まで待つ。
    /// タイムアウト時はそのまま返し、後段の検証で失敗として検出する。
    /// </summary>
    private static async Task WaitForProjectLoadedAsync(WaapiHttpClient client, string projectPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await client.CallAsync(
                        WaapiUris.CoreObjectGet,
                        new Dictionary<string, object?> { ["waql"] = "$ from type Project" },
                        new Dictionary<string, object?> { ["return"] = new[] { "id", "filePath" } },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (result.TryGetProperty("return", out var arr)
                    && arr.ValueKind == JsonValueKind.Array
                    && arr.GetArrayLength() > 0
                    && arr[0].TryGetProperty("filePath", out var pathEl)
                    && string.Equals(pathEl.GetString(), projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (WaapiException)
            {
                // ロック中／ロード中。待って再確認する。
            }
            catch (Exception ex) when (IsTransientHttpError(ex))
            {
                // ロード中は HTTP 接続自体が一瞬落ちることがある。待って再確認する。
            }

            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wwise が排他ロック中（クローズ／ロード進行中）の間、解除されるまで呼び出しをリトライする。
    /// </summary>
    private static async Task<JsonElement> CallWithLockRetryAsync(
        WaapiHttpClient client,
        string uri,
        object? args = null,
        object? options = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (true)
        {
            try
            {
                return await client.CallAsync(uri, args, options, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WaapiException ex) when (
                DateTime.UtcNow < deadline
                && (ex.Message.Contains(WaapiUris.Locked, StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("exclusive lock", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                DateTime.UtcNow < deadline && IsTransientHttpError(ex))
            {
                // クローズ／ロード直後は HTTP 接続が一瞬落ちることがある。リトライする。
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>WAQL で 1 件だけ取得し、指定の return フィールド（文字列）を返す。</summary>
    private static async Task<string?> QuerySingleReturnStringAsync(
        WaapiHttpClient client,
        string waql,
        string field,
        CancellationToken cancellationToken)
    {
        var result = await client.CallAsync(
                WaapiUris.CoreObjectGet,
                new Dictionary<string, object?> { ["waql"] = waql },
                new Dictionary<string, object?> { ["return"] = new[] { "id", field } },
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("return", out var arr)
            || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0)
        {
            return null;
        }

        return arr[0].TryGetProperty(field, out var el) ? el.GetString() : null;
    }

    /// <summary>再オープン後の MusicClip から Real64 プロパティを読み戻す。</summary>
    private static async Task<double?> QueryClipReal64Async(
        WaapiHttpClient client,
        string clipId,
        string returnField)
    {
        var result = await CallWithLockRetryAsync(
                client,
                WaapiUris.CoreObjectGet,
                new Dictionary<string, object?> { ["waql"] = $"$ \"{clipId}\"" },
                new Dictionary<string, object?> { ["return"] = new[] { "id", returnField } })
            .ConfigureAwait(false);
        if (!result.TryGetProperty("return", out var arr)
            || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0)
        {
            return null;
        }

        return arr[0].TryGetProperty(returnField, out var el)
               && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : null;
    }
}
