using TimeTracking.Models;
using TimeTracking.Repositories;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

public class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;

    public TagService(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public Task<List<Tag>> GetAllAsync() => _tagRepository.GetAllAsync();
}
