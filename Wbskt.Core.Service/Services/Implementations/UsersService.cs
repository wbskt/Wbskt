using Wbskt.Common.Contracts;
using Wbskt.Common.Providers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class UsersService(ILogger<UsersService> logger, IUsersProvider usersProvider) : IUsersService
{
    private readonly ILogger<UsersService> logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IUsersProvider usersProvider = usersProvider ?? throw new ArgumentNullException(nameof(usersProvider));

    public User AddUser(User user)
    {
        user.UserId = usersProvider.Insert(user);
        return user;
    }

    public int FindUserIdByEmailId(string emailId)
    {
        return usersProvider.FindByEmailId(emailId);
    }

    public User GetUserByEmailId(string emailId)
    {
        return usersProvider.GetByEmailId(emailId)!;
    }

    public User GetUserById(int userId)
    {
        return usersProvider.GetById(userId)!;
    }
}
