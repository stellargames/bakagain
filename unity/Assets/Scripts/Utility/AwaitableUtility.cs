namespace BakAgain.Utility {
    using UnityEngine;

    public static class AwaitableUtility {
        private static readonly AwaitableCompletionSource CompletedSource = new();

        public static Awaitable Completed {
            get {
                CompletedSource.SetResult();
                Awaitable awaitable = CompletedSource.Awaitable;
                CompletedSource.Reset();

                return awaitable;
            }
        }
    }
}