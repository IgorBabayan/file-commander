namespace File.Commander.Application.IDialogService;

public interface IDialogService
{
    Task<TResult?> ShowDialogAsync<TViewModel, TResult>(TViewModel viewModel)
        where TViewModel : ViewModelBase;
}