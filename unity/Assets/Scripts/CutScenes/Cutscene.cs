namespace BakAgain.CutScenes {
    using GameData.Resources.Animation;
    using System.Collections.Generic;

    internal class Cutscene {
        public Cutscene(List<AnimatorScript> scripts, List<Frame> frames) {
            Scripts = scripts;
            Frames = frames;
        }

        public List<AnimatorScript> Scripts { get; }
        public List<Frame> Frames { get; }
    }
}