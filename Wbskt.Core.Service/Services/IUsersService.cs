using Wbskt.Common.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IUsersService
{
    User GetUserById(int userId);

    User GetUserByEmailId(string emailId);

    User AddUser(User user);

    int FindUserIdByEmailId(string emailId);
}
