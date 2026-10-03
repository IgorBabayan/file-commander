using CommunityToolkit.Mvvm.ComponentModel;

namespace File.Commander.Presentation.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed;

    protected virtual void OnDispose() { }
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        OnDispose();
        GC.SuppressFinalize(this);
    }
}