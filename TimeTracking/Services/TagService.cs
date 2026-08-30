using System.Text.RegularExpressions;
using TimeTracking.Models;
using TimeTracking.Repositories;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

public partial class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;

    public TagService(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public Task<List<Tag>> GetAllAsync() => _tagRepository.GetAllAsync();

    public Task<Tag?> GetByIdAsync(int id) => _tagRepository.GetByIdAsync(id);

    public async Task<Tag> CreateAsync(string name, string? description, string color)
    {
        ValidateName(name);
        ValidateColor(color);

        var now = DateTime.UtcNow;
        var tag = new Tag
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Color = color,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _tagRepository.AddAsync(tag);
        return tag;
    }

    public async Task UpdateAsync(int id, string name, string? description, string color)
    {
        ValidateName(name);
        ValidateColor(color);

        var tag = await _tagRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Tag {id} não encontrada.");

        tag.Name = name.Trim();
        tag.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        tag.Color = color;
        tag.UpdatedAt = DateTime.UtcNow;

        await _tagRepository.UpdateAsync(tag);
    }

    public Task DeleteAsync(int id) => _tagRepository.DeleteAsync(id);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome da tag é obrigatório.", nameof(name));

        if (name.Trim().Length > 100)
            throw new ArgumentException("Nome da tag deve ter no máximo 100 caracteres.", nameof(name));
    }

    private static void ValidateColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color) || !HexColorRegex().IsMatch(color))
            throw new ArgumentException("Cor deve ser um hexadecimal válido (#RRGGBB).", nameof(color));
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();
}
