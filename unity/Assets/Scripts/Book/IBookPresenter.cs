namespace BakAgain.Book {
    using Cysharp.Threading.Tasks;

    public interface IBookPresenter {
        UniTask<bool> ShowBookAsync(string bokFileName);
        void Cancel();
    }
}
