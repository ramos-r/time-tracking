using CommunityToolkit.Mvvm.Input;
using TimeTracking.ViewModels;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes da Seção 68 (agrupamento retrátil) no nível da ViewModel de apresentação: o
/// comportamento de expandir/recolher e a expansão forçada por timer ativo.
/// </summary>
public class DayGroupViewModelTests
{
    private static DayGroupViewModel CreateGroup(bool isExpanded, params TaskListItemViewModel[] tasks)
    {
        var noOp = new RelayCommand(() => { });
        return new DayGroupViewModel(
            DateTime.Today,
            isToday: true,
            tasks,
            isExpanded,
            TimeSpan.Zero,
            noOp,
            noOp,
            noOp,
            noOp,
            noOp,
            isSelectionMode: false);
    }

    private static TaskListItemViewModel CreateTaskItem(int id, string name) =>
        new(new DomainTask { Id = id, Name = name });

    [Fact]
    public void Collapsing_A_Group_Hides_Cards_But_Keeps_Them_In_The_Model()
    {
        var task = CreateTaskItem(1, "Tarefa");
        var group = CreateGroup(isExpanded: true, task);

        group.ToggleExpandCommand.Execute(null);

        Assert.False(group.IsExpanded);
        Assert.Single(group.Tasks); // os cards continuam no modelo — só a apresentação esconde
    }

    [Fact]
    public void Expanding_A_Collapsed_Group_Makes_Cards_Visible_Again()
    {
        var task = CreateTaskItem(1, "Tarefa");
        var group = CreateGroup(isExpanded: false, task);

        group.ToggleExpandCommand.Execute(null);

        Assert.True(group.IsExpanded);
        Assert.Single(group.Tasks);
    }

    [Fact]
    public void Group_Auto_Expands_When_It_Gains_A_Running_Task()
    {
        var task = CreateTaskItem(1, "Tarefa");
        var group = CreateGroup(isExpanded: false, task);

        group.HasRunningTask = true;

        Assert.True(group.IsExpanded);
    }

    [Fact]
    public void Manually_Collapsing_A_Group_With_A_Running_Task_Is_Ignored()
    {
        var task = CreateTaskItem(1, "Tarefa");
        var group = CreateGroup(isExpanded: true, task);
        group.HasRunningTask = true;

        group.ToggleExpandCommand.Execute(null);

        Assert.True(group.IsExpanded); // Seção 68, item 7: não pode esconder a tarefa ativa
    }
}
