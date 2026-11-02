using System;

namespace MeoVision.Application.Auth;

public interface ICurrentUserService
{
    UserLevel Level { get; }

    event EventHandler? LevelChanged;

    bool TryLoginEngineer(string password);

    void LogoutToOperator();
}
