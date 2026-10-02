namespace BakAgain.CutScenes {
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Parses and processes game scripts containing conditional logic and scene commands.
    /// </summary>
    public class ScriptProcessor {
        private const string IfPrefix = "IF ";
        private const string And = "AND";
        private const string Else = "ELSE";
        private const string EndIf = "END IF";
        private const string StartScene = "START scene_";
        private const string ContinueScene = "CONTINUE scene_";
        private const string StopScene = "STOP scene_";
        private const string LogicalAnd = " AND ";

        /// <summary>
        /// Parses and processes a script based on the current chapter and played scenes.
        /// </summary>
        /// <param name="script">The script to parse</param>
        /// <param name="chapterNumber">Current chapter number.</param>
        /// <param name="playedScenes">Set of scenes that have been played.</param>
        /// <returns>Enumerable of scene commands to execute.</returns>
        public static IEnumerable<(CutsceneAction, int)> Process(string script, int chapterNumber, HashSet<int> playedScenes) {
            if (playedScenes == null) throw new ArgumentNullException(nameof(playedScenes));
            if (string.IsNullOrEmpty(script)) {
                yield break;
            }

            string[] lines = script.Split('\n');
            var skipStack = new Stack<bool>();
            var blockExecutedStack = new Stack<bool>();
            int nestedLevel = 0;
            int currentSkipLevel = -1;

            for (int i = 0; i < lines.Length; i++) {
                string trimmedLine = lines[i].Trim();

                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Handle IF conditions
                if (trimmedLine.StartsWith(IfPrefix)) {
                    nestedLevel++;

                    // If we're already skipping at a higher level, continue skipping
                    if (currentSkipLevel != -1 && nestedLevel > currentSkipLevel) {
                        skipStack.Push(true);
                        blockExecutedStack.Push(false);

                        continue;
                    }

                    // Collect all conditions (including those after AND)
                    var conditions = new List<string> {trimmedLine};
                    while (i + 2 < lines.Length &&
                           lines[i + 1].Trim() == And &&
                           lines[i + 2].Trim().StartsWith(IfPrefix)) {
                        conditions.Add(lines[i + 2].Trim());
                        i += 2;
                    }

                    bool allConditionsMet = conditions
                        .Select(condition => EvaluateCondition(condition, chapterNumber, playedScenes))
                        .All(conditionResult => conditionResult);

                    bool shouldSkip = !allConditionsMet || (skipStack.Count > 0 && skipStack.Peek());
                    skipStack.Push(shouldSkip);
                    if (shouldSkip && currentSkipLevel == -1) {
                        currentSkipLevel = nestedLevel;
                    }
                    blockExecutedStack.Push(false);

                    continue;
                }

                // Handle ELSE
                if (trimmedLine == Else) {
                    if (skipStack.Count > 0) {
                        bool wasSkipping = skipStack.Pop();
                        bool wasExecuted = blockExecutedStack.Pop();

                        // If we're in a skipped outer block, keep skipping
                        if (currentSkipLevel != -1 && nestedLevel > currentSkipLevel) {
                            skipStack.Push(true);
                        } else {
                            // Execute ELSE block only if IF block was skipped and not executed
                            bool shouldSkipElse = wasExecuted || !wasSkipping;
                            skipStack.Push(shouldSkipElse);
                            if (!shouldSkipElse && currentSkipLevel == nestedLevel) {
                                currentSkipLevel = -1;
                            }
                        }

                        blockExecutedStack.Push(false);
                    }

                    continue;
                }

                // Handle END IF
                if (trimmedLine == EndIf) {
                    if (skipStack.Count > 0) {
                        skipStack.Pop();
                        blockExecutedStack.Pop();
                        if (nestedLevel == currentSkipLevel) {
                            currentSkipLevel = -1;
                        }
                        nestedLevel--;
                    }

                    continue;
                }

                // Skip lines if we're in a skipped block
                if (skipStack.Count > 0 && skipStack.Peek())
                    continue;

                // Handle scene commands
                if (TryProcessSceneCommand(trimmedLine, out (CutsceneAction, int) command)) {
                    yield return command;
                    // Mark the current block as executed
                    if (blockExecutedStack.Count > 0) {
                        blockExecutedStack.Pop();
                        blockExecutedStack.Push(true);
                    }
                }
            }
        }

        /// <summary>
        /// Whether a command plays its scene. CONTINUE (0x2000) is <c>anim_script_object_restart</c>
        /// (ANIMSCR.C:649): rewind to the scene's start, then arm it as START (0x2005) does — so from a
        /// fresh state both play the scene from its first frame. Only g_town's LAMUT-MAIN ships one.
        /// </summary>
        public static bool Plays(CutsceneAction action) =>
            action == CutsceneAction.Start || action == CutsceneAction.Continue;

        private static bool TryProcessSceneCommand(string line, out (CutsceneAction, int) command) {
            command = default;
            try {
                if (line.StartsWith(StartScene)) {
                    int sceneNumber = ParseSceneNumber(line, StartScene);
                    command = (CutsceneAction.Start, sceneNumber);

                    return true;
                }
                if (line.StartsWith(ContinueScene)) {
                    int sceneNumber = ParseSceneNumber(line, ContinueScene);
                    command = (CutsceneAction.Continue, sceneNumber);

                    return true;
                }
                if (line.StartsWith(StopScene)) {
                    int sceneNumber = ParseSceneNumber(line, StopScene);
                    command = (CutsceneAction.Stop, sceneNumber);

                    return true;
                }

                return false;
            } catch (FormatException) {
                return false;
            }
        }

        private static int ParseSceneNumber(string line, string prefix) {
            ReadOnlySpan<char> sceneNumberStr = line.AsSpan(prefix.Length).Trim();

            return int.Parse(sceneNumberStr);
        }

        private static bool EvaluateCondition(string condition, int chapterNumber, HashSet<int> playedScenes) {
            // Handle single condition
            if (!condition.Contains(LogicalAnd)) {
                return EvaluateSingleCondition(condition, chapterNumber, playedScenes);
            }

            // Handle compound conditions with AND
            return condition.Split(LogicalAnd, StringSplitOptions.RemoveEmptyEntries)
                .All(singleCondition => EvaluateSingleCondition(singleCondition.Trim(), chapterNumber, playedScenes));
        }

        private static bool EvaluateSingleCondition(string condition, int chapterNumber, HashSet<int> playedScenes) {
            if (condition.StartsWith("IF CHAPTER >= ")) {
                int targetChapter = ParseNumber(condition, "IF CHAPTER >= ");

                return chapterNumber >= targetChapter;
            }

            if (condition.StartsWith("IF CHAPTER <= ")) {
                int targetChapter = ParseNumber(condition, "IF CHAPTER <= ");

                return chapterNumber <= targetChapter;
            }

            if (condition.StartsWith("IF NOT PLAYED scene_")) {
                int targetScene = ParseNumber(condition, "IF NOT PLAYED scene_");

                return !playedScenes.Contains(targetScene);
            }

            if (condition.StartsWith("IF PLAYED scene_")) {
                int targetScene = ParseNumber(condition, "IF PLAYED scene_");

                return playedScenes.Contains(targetScene);
            }

            return false;
        }

        private static int ParseNumber(string text, string prefix) {
            ReadOnlySpan<char> numberStr = text.AsSpan(prefix.Length).Trim();

            return int.Parse(numberStr);
        }
    }
}