using TimeTracking.Models;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

public interface ITagService
{
    Task<List<Tag>> GetAllAsync();
}
