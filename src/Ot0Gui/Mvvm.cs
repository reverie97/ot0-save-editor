using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Ot0Gui;

abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(name);
        return true;
    }
}

sealed class Command(Action<object?> run, Func<bool>? canRun = null) : ICommand
{
    public Command(Action run, Func<bool>? canRun = null) : this(_ => run(), canRun) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public void Execute(object? parameter) => run(parameter);
}
