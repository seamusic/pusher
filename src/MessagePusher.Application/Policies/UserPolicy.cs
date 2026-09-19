using MessagePusher.Domain;
using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Policies;

public sealed class UserPolicy
{
    public void EnsureCanRead(int actorRole, User target)
    {
        if (actorRole <= target.Role)
            throw new BusinessException("无权获取同级或更高等级用户的信息");
    }

    public void EnsureCanUpdate(int actorRole, User origin, int updatedRole)
    {
        if (actorRole <= origin.Role)
            throw new BusinessException("无权更新同权限等级或更高权限等级的用户信息");
        if (actorRole <= updatedRole)
            throw new BusinessException("无权将其他用户权限等级提升到大于等于自己的权限等级");
    }

    public void EnsureCanDelete(int actorRole, User target)
    {
        if (actorRole <= target.Role)
            throw new BusinessException("无权删除同权限等级或更高权限等级的用户");
    }

    public void EnsureCanCreate(int actorRole, int createdRole)
    {
        if (createdRole >= actorRole)
            throw new BusinessException("无法创建权限大于等于自己的用户");
    }

    public void EnsureCanManage(int actorRole, User target)
    {
        if (actorRole <= target.Role && actorRole != Roles.Root)
            throw new BusinessException("无权更新同权限等级或更高权限等级的用户信息");
    }

    public void EnsureRootForPromote(int actorRole)
    {
        if (actorRole != Roles.Root)
            throw new BusinessException("普通管理员用户无法提升其他用户为管理员");
    }

    public static bool CanSendEmailToOthers(User user)
    {
        return user.SendEmailToOthers == UserPreference.Allowed || user.Role >= Roles.Admin;
    }
}
