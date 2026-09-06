using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace DominionWars.Unity.UI
{

/// <summary>
/// Optional Player-only visual evidence hook. It is inert unless the process
/// receives an explicit absolute PNG path, and it never participates in the
/// normal screen flow or gameplay state.
/// </summary>
public sealed class RuntimeVisualSmokeOptions
{
    public RuntimeVisualSmokeOptions(string outputPath, bool quitAfterCapture)
        : this(outputPath, quitAfterCapture, false)
    {
    }

    public RuntimeVisualSmokeOptions(
        string outputPath,
        bool quitAfterCapture,
        bool captureBattle)
    {
        OutputPath = outputPath;
        QuitAfterCapture = quitAfterCapture;
        CaptureBattle = captureBattle;
    }

    public string OutputPath { get; }
    public bool QuitAfterCapture { get; }
    public bool CaptureBattle { get; }
}

[DisallowMultipleComponent]
public sealed class RuntimePlayerVisualSmoke : MonoBehaviour
{
    public const string PathArgument = "-dwVisualSmokePath";
    public const string QuitArgument = "-dwVisualSmokeQuit";
    public const string BattleArgument = "-dwVisualSmokeBattle";
    public const string DefaultObjectName = "DominionWarsRuntimeVisualSmoke";
    public const string StartedLogPrefix = "Dominion Wars visual smoke requested: ";
    public const string CapturedLogPrefix = "Dominion Wars visual smoke captured: ";
    public const string FailedLogPrefix = "Dominion Wars visual smoke failed: ";
    public const float ReadyTimeoutSeconds = 30f;
    public const float CaptureWriteTimeoutSeconds = 10f;
    public const float StableWriteSeconds = 0.25f;
    public const int MaxCaptureDimension = 32768;

    private static readonly byte[] PngSignature =
    {
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
    };

    private RuntimeVisualSmokeOptions _options;

    public static bool TryParseCommandLine(
        IReadOnlyList<string> arguments,
        out RuntimeVisualSmokeOptions options,
        out string failureReason)
    {
        options = null;
        failureReason = string.Empty;
        if (arguments == null) return false;

        var pathArgumentIndex = -1;
        var quitAfterCapture = false;
        var captureBattle = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, PathArgument, StringComparison.Ordinal))
            {
                if (pathArgumentIndex >= 0)
                {
                    failureReason = "the visual smoke path argument was supplied more than once";
                    return false;
                }

                pathArgumentIndex = index;
            }
            else if (string.Equals(argument, QuitArgument, StringComparison.Ordinal))
            {
                quitAfterCapture = true;
            }
            else if (string.Equals(argument, BattleArgument, StringComparison.Ordinal))
            {
                captureBattle = true;
            }
        }

        // The hook is deliberately opt-in. No path flag means no object,
        // no coroutine, no screenshot, and no change to normal Player flow.
        if (pathArgumentIndex < 0) return false;
        if (pathArgumentIndex == arguments.Count - 1)
        {
            failureReason = PathArgument + " requires an absolute PNG output path";
            return false;
        }

        var rawPath = arguments[pathArgumentIndex + 1];
        if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathFullyQualified(rawPath))
        {
            failureReason = "the visual smoke output path must be fully qualified";
            return false;
        }

        string outputPath;
        try
        {
            outputPath = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is PathTooLongException)
        {
            failureReason = "the visual smoke output path is invalid";
            return false;
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "the visual smoke output path must end in .png";
            return false;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            failureReason = "the visual smoke output directory must already exist";
            return false;
        }

        if (Directory.Exists(outputPath))
        {
            failureReason = "the visual smoke output path is a directory";
            return false;
        }

        // ScreenCapture writes through Unity's native path and can overwrite
        // an existing file. Refuse that case so a diagnostic run cannot erase
        // prior evidence or another user's artifact.
        if (File.Exists(outputPath))
        {
            failureReason = "the visual smoke output file already exists";
            return false;
        }

        options = new RuntimeVisualSmokeOptions(outputPath, quitAfterCapture, captureBattle);
        return true;
    }

    /// <summary>
    /// Validates only the minimum structural evidence required from a captured
    /// framebuffer: a complete PNG signature and a positive, reasonably sized
    /// IHDR canvas. It deliberately makes no claim about the semantic visual
    /// contents of the pixels.
    /// </summary>
    public static bool TryValidateCapturedPng(
        string path,
        out int width,
        out int height,
        out string failureReason)
    {
        width = 0;
        height = 0;
        failureReason = string.Empty;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 33)
            {
                failureReason = "the captured PNG is shorter than a complete IHDR chunk";
                return false;
            }

            var header = new byte[24];
            if (stream.Read(header, 0, header.Length) != header.Length)
            {
                failureReason = "the captured PNG header could not be read completely";
                return false;
            }

            for (var index = 0; index < PngSignature.Length; index++)
            {
                if (header[index] == PngSignature[index]) continue;
                failureReason = "the captured file does not have a PNG signature";
                return false;
            }

            if (ReadUInt32BigEndian(header, 8) != 13 ||
                header[12] != (byte)'I' ||
                header[13] != (byte)'H' ||
                header[14] != (byte)'D' ||
                header[15] != (byte)'R')
            {
                failureReason = "the captured PNG does not begin with a valid IHDR chunk";
                return false;
            }

            var parsedWidth = ReadUInt32BigEndian(header, 16);
            var parsedHeight = ReadUInt32BigEndian(header, 20);
            if (parsedWidth == 0 || parsedHeight == 0 ||
                parsedWidth > MaxCaptureDimension || parsedHeight > MaxCaptureDimension)
            {
                failureReason = "the captured PNG has invalid IHDR dimensions";
                return false;
            }

            width = (int)parsedWidth;
            height = (int)parsedHeight;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is FileNotFoundException ||
            exception is DirectoryNotFoundException ||
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
            failureReason = "the captured PNG could not be opened for validation";
            return false;
        }
    }

    /// <summary>
    /// Starts the opt-in visual smoke coroutine from the current Player's
    /// command line. This is called only after RuntimeScreenFlow has presented
    /// its TITLE shell. Invalid opt-in arguments fail with a diagnostic log and
    /// leave the normal game flow untouched.
    /// </summary>
    public static bool TryStartFromCommandLine()
    {
        if (!TryParseCommandLine(
                Environment.GetCommandLineArgs(),
                out var options,
                out var failureReason))
        {
            if (!string.IsNullOrWhiteSpace(failureReason))
                Debug.LogError(FailedLogPrefix + failureReason);
            return false;
        }

        if (UnityEngine.Object.FindFirstObjectByType<RuntimePlayerVisualSmoke>() != null)
            return true;

        var smokeObject = new GameObject(DefaultObjectName);
        DontDestroyOnLoad(smokeObject);
        var smoke = smokeObject.AddComponent<RuntimePlayerVisualSmoke>();
        smoke._options = options;
        smoke.StartCoroutine(smoke.CaptureWhenReady());
        return true;
    }

    private IEnumerator CaptureWhenReady()
    {
        Debug.Log(StartedLogPrefix + _options.OutputPath);
        var deadline = Time.realtimeSinceStartup + ReadyTimeoutSeconds;
        RuntimeScreenFlow flow = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            flow = UnityEngine.Object.FindFirstObjectByType<RuntimeScreenFlow>();
            if (flow != null && flow.IsPresentationReady) break;
            yield return null;
        }

        if (flow == null || !flow.IsPresentationReady)
        {
            Fail("TITLE shell was not ready within " + ReadyTimeoutSeconds + " seconds.");
            yield break;
        }

        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Fail("a graphical Player is required; remove -batchmode and -nographics");
            yield break;
        }

        if (_options.CaptureBattle)
        {
            flow.Navigate(RuntimeScreenId.MatchSetup);
            flow.RequestStartMatch();
            var battleDeadline = Time.realtimeSinceStartup + ReadyTimeoutSeconds;
            while (Time.realtimeSinceStartup < battleDeadline)
            {
                if (flow.CurrentScreen == RuntimeScreenId.Battle
                    && flow.BattlePanel != null
                    && flow.BattlePanel.isActiveAndEnabled
                    && flow.BattlePanel.Adapter != null
                    && flow.BattlePanel.Adapter.Presentation.Snapshot != null)
                    break;
                yield return null;
            }

            if (flow.CurrentScreen != RuntimeScreenId.Battle
                || flow.BattlePanel == null
                || flow.BattlePanel.Adapter == null
                || flow.BattlePanel.Adapter.Presentation.Snapshot == null)
            {
                Fail("BATTLE screen was not ready within " + ReadyTimeoutSeconds + " seconds.");
                yield break;
            }

            // Let layout, data-backed card faces and placeholder textures
            // finish one visible frame before capturing the battle surface.
            yield return null;
        }

        if (File.Exists(_options.OutputPath))
        {
            Fail("the visual smoke output file appeared before capture");
            yield break;
        }

        // Capture after a rendered frame so the evidence comes from the game
        // framebuffer rather than the desktop, lock screen, or editor chrome.
        yield return new WaitForEndOfFrame();
        try
        {
            ScreenCapture.CaptureScreenshot(_options.OutputPath, 1);
        }
        catch (Exception exception)
        {
            Fail("ScreenCapture threw " + exception.GetType().Name + ".");
            yield break;
        }

        var writeDeadline = Time.realtimeSinceStartup + CaptureWriteTimeoutSeconds;
        var lastLength = -1L;
        var stableSince = -1f;
        var validationFailure = "the output file was not created";
        var captureWidth = 0;
        var captureHeight = 0;
        var captureValidated = false;
        while (Time.realtimeSinceStartup < writeDeadline)
        {
            long currentLength;
            try
            {
                currentLength = File.Exists(_options.OutputPath)
                    ? new FileInfo(_options.OutputPath).Length
                    : -1L;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                currentLength = -1L;
                validationFailure = "the output file could not be inspected while it was being written";
            }

            if (currentLength <= 0)
            {
                lastLength = currentLength;
                stableSince = -1f;
            }
            else if (currentLength != lastLength)
            {
                lastLength = currentLength;
                stableSince = Time.realtimeSinceStartup;
            }
            else
            {
                if (stableSince < 0f)
                    stableSince = Time.realtimeSinceStartup;

                if (Time.realtimeSinceStartup - stableSince >= StableWriteSeconds)
                {
                    captureValidated = TryValidateCapturedPng(
                        _options.OutputPath,
                        out captureWidth,
                        out captureHeight,
                        out validationFailure);
                    if (captureValidated) break;
                }
            }

            yield return null;
        }

        if (!captureValidated)
        {
            Fail(
                "ScreenCapture did not produce a stable, valid PNG within " +
                CaptureWriteTimeoutSeconds + " seconds: " + validationFailure + ".");
            yield break;
        }

        Debug.Log(
            CapturedLogPrefix + _options.OutputPath +
            " (framebuffer " + captureWidth + "x" + captureHeight + ")");
        if (_options.QuitAfterCapture && !Application.isEditor)
            Application.Quit(0);

        Destroy(gameObject);
    }

    private void Fail(string message)
    {
        Debug.LogError(FailedLogPrefix + message);
        if (_options != null && _options.QuitAfterCapture && !Application.isEditor)
            Application.Quit(1);
        Destroy(gameObject);
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) |
               ((uint)buffer[offset + 1] << 16) |
               ((uint)buffer[offset + 2] << 8) |
               buffer[offset + 3];
    }
}
}
