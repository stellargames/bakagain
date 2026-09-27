namespace BakAgain.Audio {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;

    /// <summary>
    /// Plays the sound sequence a spell's effect arm makes.
    /// </summary>
    /// <remarks>
    /// <b>The sequencing lives here, not in the rules.</b> `SpellEffectArmSound` says which cues and
    /// how many; the waits and the hold-then-stop need async, and `CombatRuntime` is a plain rules
    /// object with no UniTask. So the model stays engine-free and this is the one place that knows
    /// about time.
    ///
    /// <para><b>Fire-and-forget on purpose.</b> The original blocks its whole frame loop on the
    /// storm's four waits; nothing downstream of a cast is waiting on the noise, so blocking the
    /// turn on it would be a worse trade than letting it play out.</para>
    /// </remarks>
    public static class SpellArmAudio {
        /// <summary>A tick, in milliseconds. The storm's gap is <c>g_nFrameTickCountdown</c>, which
        /// the IRQ0 handler decrements at ~236.7 Hz (TIMER.ASM) — about 4.2 ms, not the 40 ms this
        /// used to guess, which made the four cracks up to ten times too far apart.</summary>
        private static readonly double TickMilliseconds =
            GameData.Resources.Combat.SpellVisuals.IrqSeconds * 1000.0;

        /// <summary>Play the arm for this effect kind, if it has one.</summary>
        public static void Play(int animationEffectType, System.Func<int, int> rnd) {
            if (!SpellEffectArmSound.HasSequence(animationEffectType)) {
                return;
            }
            if (animationEffectType == SpellEffectArmSound.StormFlashKind) {
                StormAsync(rnd).Forget();
            } else if (animationEffectType == SpellEffectArmSound.ParticleBlastKind) {
                ParticleBlastAsync().Forget();
            } else {
                // Touch of Lims-Kragma: one shot, no hold and no sequence. Its arm's DEATH is dead
                // code in the original — see SpellEffectArmSound.WalkWithSoundKind — so the cue and
                // the walk are the whole of it.
                MenuSoundService.Instance?.Play(SpellEffectArmSound.TouchCue);
            }
        }

        /// <summary>Skyfire: hold the static, crack four times, then release it.</summary>
        private static async UniTaskVoid StormAsync(System.Func<int, int> rnd) {
            MenuSoundService svc = MenuSoundService.Instance;
            if (svc == null) {
                return;
            }
            svc.Play(SpellEffectArmSound.StaticCue);
            for (var flash = 0; flash < SpellEffectArmSound.StormFlashCount; flash++) {
                svc.Play(SpellEffectArmSound.ThunderCue);
                await UniTask.Delay(System.TimeSpan.FromMilliseconds(SpellEffectArmSound.FlashGapTicks(rnd) * TickMilliseconds));
            }
            // The static is stopped explicitly; it does not simply run out.
            svc.Stop(SpellEffectArmSound.StaticCue);
        }

        /// <summary>Mind Melt: logostar, thunder, cut it, thunder again.</summary>
        private static async UniTaskVoid ParticleBlastAsync() {
            MenuSoundService svc = MenuSoundService.Instance;
            if (svc == null) {
                return;
            }
            svc.Play(SpellEffectArmSound.LogostarCue);
            svc.Play(SpellEffectArmSound.ThunderCue);
            await UniTask.Delay(System.TimeSpan.FromMilliseconds(TickMilliseconds));
            // *** THE STOP IS BETWEEN THE TWO THUNDERS, NOT AFTER THEM. *** The arm cuts the first
            // crack short and immediately starts another, which is what makes it a double report
            // rather than one long roll.
            svc.Stop(SpellEffectArmSound.ThunderCue);
            svc.Play(SpellEffectArmSound.ThunderCue);
        }
    }
}
