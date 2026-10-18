using Locksy.Models;

namespace Locksy.Factories;

public interface IMainPageFactory
{
    MainPage Create(bool startHidden = false, IEnumerable<TodoItem>? initialTasks = null);
}
