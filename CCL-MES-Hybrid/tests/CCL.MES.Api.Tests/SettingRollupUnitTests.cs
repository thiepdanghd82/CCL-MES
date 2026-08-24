using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>P10.7g — pure rollup unit tests for
/// <see cref="SettingCheckService.Rollup"/>. No I/O.</summary>
public sealed class SettingRollupUnitTests
{
    private static WoSettingCheckItem Item(string kind, PrepressCheckStatus status, bool applicable = true)
        => new() { ProcessKind = kind, Status = status, Applicable = applicable };

    [Fact]
    public void Ready_true_when_all_applicable_of_applicable_process_are_Ok()
    {
        var items = new[]
        {
            Item("Print", PrepressCheckStatus.Ok),
            Item("Cut", PrepressCheckStatus.Ok),
        };
        Assert.True(SettingCheckService.Rollup(items, hasPrint: true, hasCut: true));
    }

    [Fact]
    public void Ready_false_when_any_applicable_is_Pending()
    {
        var items = new[]
        {
            Item("Print", PrepressCheckStatus.Ok),
            Item("Cut", PrepressCheckStatus.Pending),
        };
        Assert.False(SettingCheckService.Rollup(items, hasPrint: true, hasCut: true));
    }

    [Fact]
    public void Ready_false_when_any_applicable_is_Ng()
    {
        var items = new[]
        {
            Item("Print", PrepressCheckStatus.Ng),
        };
        Assert.False(SettingCheckService.Rollup(items, hasPrint: true, hasCut: false));
    }

    [Fact]
    public void NA_items_excluded_from_the_guard()
    {
        var items = new[]
        {
            Item("Print", PrepressCheckStatus.Ok),
            Item("Print", PrepressCheckStatus.Pending, applicable: false), // N/A → ignored
        };
        Assert.True(SettingCheckService.Rollup(items, hasPrint: true, hasCut: false));
    }

    [Fact]
    public void Only_the_applicable_process_counts()
    {
        var items = new[]
        {
            Item("Print", PrepressCheckStatus.Ok),
            Item("Cut", PrepressCheckStatus.Pending), // Cut not applicable → ignored
        };
        Assert.True(SettingCheckService.Rollup(items, hasPrint: true, hasCut: false));
    }

    [Fact]
    public void Empty_applicable_set_is_not_ready()
    {
        var items = new[] { Item("Cut", PrepressCheckStatus.Ok) };
        // hasPrint true but no Print items materialised yet → not ready.
        Assert.False(SettingCheckService.Rollup(items, hasPrint: true, hasCut: false));
    }
}
