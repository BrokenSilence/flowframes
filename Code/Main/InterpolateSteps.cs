﻿using Flowframes.AudioVideo;
using Flowframes.Data;
using Flowframes.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flowframes.Main
{
    using static Interpolate;

    class InterpolateSteps
    {
        public enum Step { ExtractScnChanges, ExtractFrames, Interpolate, CreateVid, Reset }

        //public static string current.inPath;
        //public static string currentOutPath;
        //public static string current.interpFolder;
        //public static AI currentAi;
        //public static OutMode currentOutMode;

        public static async Task Run(string step)
        {
            Logger.Log($"[SBS] Running step '{step}'", true);
            canceled = false;
            Program.mainForm.SetWorking(true);
            current = Program.mainForm.GetCurrentSettings();

            if (!InterpolateUtils.InputIsValid(current.inPath, current.outPath, current.outFps, current.interpFactor, current.outMode)) return;     // General input checks

            if (step.Contains("Extract Scene Changes"))
            {
                if (!current.inputIsFrames)        // Input is video - extract frames first
                    await ExtractSceneChanges();
                else
                    InterpolateUtils.ShowMessage("Scene changes can only be extracted from videos, not frames!", "Error");
            }

            if (step.Contains("Extract Frames"))
            {
                await GetFrames();
            }

            if (step.Contains("Run Interpolation"))
                await DoInterpolate();

            if (step.Contains("Export"))
                await CreateOutputVid();

            if (step.Contains("Cleanup"))
                await Reset();

            Program.mainForm.SetWorking(false);
            Logger.Log("Done running this step.");
        }

        public static async Task ExtractSceneChanges()
        {
            string scenesPath = Path.Combine(current.tempFolder, Paths.scenesDir);
            if (!IOUtils.TryDeleteIfExists(scenesPath))
            {
                InterpolateUtils.ShowMessage("Failed to delete existing scenes folder - Make sure no file is opened in another program!", "Error");
                return;
            }
            Program.mainForm.SetStatus("Extracting scenes from video...");
            await FFmpegCommands.ExtractSceneChanges(current.inPath, scenesPath, current.inFps);
            await Task.Delay(10);
        }

        public static async Task ExtractVideoFrames()
        {
            if (!IOUtils.TryDeleteIfExists(current.framesFolder))
            {
                InterpolateUtils.ShowMessage("Failed to delete existing frames folder - Make sure no file is opened in another program!", "Error");
                return;
            }

            currentInputFrameCount = await InterpolateUtils.GetInputFrameCountAsync(current.inPath);
            AiProcess.filenameMap.Clear();

            await ExtractFrames(current.inPath, current.framesFolder, false, true);
        }

        public static async Task DoInterpolate()
        {
            current.framesFolder = Path.Combine(current.tempFolder, Paths.framesDir);
            if (!Directory.Exists(current.framesFolder) || IOUtils.GetAmountOfFiles(current.framesFolder, false, "*.png") < 2)
            {
                InterpolateUtils.ShowMessage("There are no extracted frames that can be interpolated!\nDid you run the extraction step?", "Error");
                return;
            }
            if (!IOUtils.TryDeleteIfExists(current.interpFolder))
            {
                InterpolateUtils.ShowMessage("Failed to delete existing frames folder - Make sure no file is opened in another program!", "Error");
                return;
            }

            currentInputFrameCount = await InterpolateUtils.GetInputFrameCountAsync(current.inPath);

            foreach (string ini in Directory.GetFiles(current.tempFolder, "*.ini", SearchOption.TopDirectoryOnly))
                IOUtils.TryDeleteIfExists(ini);

            IOUtils.ReverseRenaming(AiProcess.filenameMap, true);   // Get timestamps back

            // TODO: Check if this works lol, remove if it does
            //if (Config.GetBool("sbsAllowAutoEnc"))
            //    nextOutPath = Path.Combine(currentOutPath, Path.GetFileNameWithoutExtension(current.inPath) + IOUtils.GetAiSuffix(current.ai, current.interpFactor) + InterpolateUtils.GetExt(current.outMode));

            await PostProcessFrames(true);

            int frames = IOUtils.GetAmountOfFiles(current.framesFolder, false, "*.png");
            int targetFrameCount = frames * current.interpFactor;
            if (canceled) return;
            Program.mainForm.SetStatus("Running AI...");
            await RunAi(current.interpFolder, current.ai, true);
            Program.mainForm.SetProgress(0);
        }

        public static async Task CreateOutputVid()
        {
            string[] outFrames = IOUtils.GetFilesSorted(current.interpFolder, $"*.{InterpolateUtils.GetOutExt()}");
            if (outFrames.Length > 0 && !IOUtils.CheckImageValid(outFrames[0]))
            {
                InterpolateUtils.ShowMessage("Invalid frame files detected!\n\nIf you used Auto-Encode, this is normal, and you don't need to run " +
                    "this step as the video was already created in the \"Interpolate\" step.", "Error");
                return;
            }

            string outPath = Path.Combine(current.outPath, Path.GetFileNameWithoutExtension(current.inPath) + IOUtils.GetCurrentExportSuffix() + FFmpegUtils.GetExt(current.outMode));
            await CreateVideo.Export(current.interpFolder, outPath, current.outMode);
        }

        public static async Task Reset()
        {
            Cleanup(current.interpFolder, true);
        }
    }
}
