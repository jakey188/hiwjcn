﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hiwjcn.Core.Domain.User;
using Lib.infrastructure.service;
using Lib.infrastructure.entity;
using Lib.ioc;
using Lib.data.ef;
using Lib.mvc.user;
using Lib.extension;
using Lib.mvc;
using Lib.infrastructure.service.user;

namespace Hiwjcn.Bll.User
{
    public interface IUserLoginService :
        IUserLoginServiceBase<UserEntity, UserOneTimeCodeEntity, RolePermissionEntity, UserRoleEntity, UserDepartmentEntity, DepartmentRoleEntity>,
        IAutoRegistered
    { }

    public class UserLoginService :
        UserLoginServiceBase<UserEntity, UserOneTimeCodeEntity, RolePermissionEntity, UserRoleEntity, UserDepartmentEntity, DepartmentRoleEntity>,
        IUserLoginService
    {
        public UserLoginService(
            IEFRepository<UserDepartmentEntity> _userDepartmentRepo,
            IEFRepository<DepartmentRoleEntity> _departmentRoleRepo,
            IEFRepository<UserEntity> _userRepo,
            IEFRepository<UserOneTimeCodeEntity> _oneTimeCodeRepo,
            IEFRepository<RolePermissionEntity> _rolePermissionRepo,
            IEFRepository<UserRoleEntity> _userRoleRepo) :
            base(_userDepartmentRepo, _departmentRoleRepo, _userRepo, _oneTimeCodeRepo, _rolePermissionRepo, _userRoleRepo)
        { }

        public override string EncryptPassword(string password)
        {
            return password.Trim().ToMD5().Trim().ToUpper();
        }

        public override async Task<List<string>> GetAutoAssignRole()
        {
            return await Task.FromResult(new List<string>() { });
        }

        public override async Task<_<UserEntity>> RegisterUser(UserEntity model)
        {
            var data = new _<UserEntity>();
            if (!model.IsValid(out var msg))
            {
                data.SetErrorMsg(msg);
                return data;
            }
            if (await this._userRepo.ExistAsync(x => x.UserName == model.UserName))
            {
                data.SetErrorMsg("用户名已存在");
                return data;
            }
            if (await this._userRepo.ExistAsync(x => x.Email == model.Email))
            {
                data.SetErrorMsg("电子邮箱已存在");
                return data;
            }
            if (await this._userRepo.ExistAsync(x => x.Phone == model.Phone))
            {
                data.SetErrorMsg("电话号码已存在");
                return data;
            }

            return await base.RegisterUser(model);
        }
    }
}