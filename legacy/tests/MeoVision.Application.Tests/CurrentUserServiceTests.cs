using FluentAssertions;
using MeoVision.Application.Auth;
using Xunit;

namespace MeoVision.Application.Tests;

public class CurrentUserServiceTests
{
    private static CurrentUserService CreateService(string password = "correct-password") =>
        new(new MeoVisionHostOptions { EngineerPassword = password });

    [Fact]
    public void Level_Should_StartAsOperator()
    {
        CurrentUserService service = CreateService();

        service.Level.Should().Be(UserLevel.Operator);
    }

    [Fact]
    public void TryLoginEngineer_Should_ReturnTrue_AndSetEngineerLevel_WhenPasswordCorrect()
    {
        CurrentUserService service = CreateService("secret123");

        bool result = service.TryLoginEngineer("secret123");

        result.Should().BeTrue();
        service.Level.Should().Be(UserLevel.Engineer);
    }

    [Fact]
    public void TryLoginEngineer_Should_ReturnFalse_AndStayOperator_WhenPasswordWrong()
    {
        CurrentUserService service = CreateService("secret123");

        bool result = service.TryLoginEngineer("wrong-password");

        result.Should().BeFalse();
        service.Level.Should().Be(UserLevel.Operator);
    }

    [Fact]
    public void LogoutToOperator_Should_ResetToOperator_AfterEngineerLogin()
    {
        CurrentUserService service = CreateService("secret123");
        service.TryLoginEngineer("secret123");

        service.LogoutToOperator();

        service.Level.Should().Be(UserLevel.Operator);
    }

    [Fact]
    public void TryLoginEngineer_Should_RaiseLevelChanged_WhenPasswordCorrect()
    {
        CurrentUserService service = CreateService("secret123");
        int raisedCount = 0;
        service.LevelChanged += (_, _) => raisedCount++;

        service.TryLoginEngineer("secret123");

        raisedCount.Should().Be(1);
    }

    [Fact]
    public void TryLoginEngineer_Should_NotRaiseLevelChanged_WhenPasswordWrong()
    {
        CurrentUserService service = CreateService("secret123");
        int raisedCount = 0;
        service.LevelChanged += (_, _) => raisedCount++;

        service.TryLoginEngineer("wrong-password");

        raisedCount.Should().Be(0);
    }
}
