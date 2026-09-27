namespace BakAgain.Tests.Editor.World {
    using System;
    using BakAgain.Core.Services;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;

    /// <summary>In-memory IPreferencesService for tests. Mutate <see cref="Current"/> directly,
    /// then call <see cref="RaiseChanged"/> to fire the event.</summary>
    internal sealed class FakePreferencesService : IPreferencesService {
        public Preferences Current { get; set; } = new Preferences("USER");
        public event Action Changed;
        public UniTask EnsureLoadedAsync() => UniTask.CompletedTask;
        public UniTask<Preferences> GetDefaultsAsync() => UniTask.FromResult(new Preferences("DEFAULT"));
        public void Apply(Preferences working) { Current = working.Clone(); Changed?.Invoke(); }
        public void RaiseChanged() => Changed?.Invoke();
    }
}
