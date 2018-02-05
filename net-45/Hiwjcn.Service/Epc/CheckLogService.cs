﻿using EPC.Core;
using EPC.Core.Entity;
using EPC.Core.Model;
using EPC.Service.InputsType;
using Hiwjcn.Core.Data;
using Hiwjcn.Core.Domain.User;
using Lib.core;
using Lib.data.ef;
using Lib.extension;
using Lib.helper;
using Lib.infrastructure;
using Lib.infrastructure.extension;
using Lib.mvc;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace EPC.Service
{
    public interface ICheckLogService : IServiceBase<CheckLogEntity>
    {
        Task<List<LogTimeGroupByDevice>> QueryLastCheckLogTime(string org_uid, string[] device_uids, DateTime after);

        Task<_<List<CheckInputDataResult>>> SubmitCheckLog(DeviceInputData model);

        Task<List<CheckLogEntity>> QueryCheckLog(string org_uid, int count);
    }

    public class CheckLogService : ServiceBase<CheckLogEntity>, ICheckLogService
    {
        private readonly IEpcRepository<CheckLogEntity> _logRepo;
        private readonly IEpcRepository<CheckLogItemEntity> _logItemRepo;
        private readonly IEpcRepository<DeviceParameterEntity> _paramRepo;
        private readonly IEpcRepository<DeviceEntity> _deviceRepo;
        private readonly IMSRepository<UserEntity> _userRepo;

        public CheckLogService(
            IEpcRepository<CheckLogEntity> _logRepo,
            IEpcRepository<CheckLogItemEntity> _logItemRepo,
            IEpcRepository<DeviceParameterEntity> _paramRepo,
            IEpcRepository<DeviceEntity> _deviceRepo,
            IMSRepository<UserEntity> _userRepo)
        {
            this._logRepo = _logRepo;
            this._logItemRepo = _logItemRepo;
            this._paramRepo = _paramRepo;
            this._deviceRepo = _deviceRepo;
            this._userRepo = _userRepo;
        }

        private void CheckInputData(DeviceInputData model)
        {
            if (!ValidateHelper.IsPlumpString(model.OrgUID))
            {
                throw new MsgException("组织UID为空");
            }
            if (!ValidateHelper.IsPlumpString(model.DeviceUID))
            {
                throw new MsgException("设备UID为空");
            }
            if (!ValidateHelper.IsPlumpString(model.UserUID))
            {
                throw new MsgException("用户UID为空");
            }
            if (!ValidateHelper.IsPlumpList(model.Data))
            {
                throw new MsgException("数据为空");
            }
            if (!model.Data.All(x => ValidateHelper.IsAllPlumpString(x.ParamUID, x.ValueJson)))
            {
                throw new MsgException("提交数据存在错误");
            }
        }

        private async Task<CheckLogEntity> PrepareCheckLog(DeviceInputData model, List<CheckLogItemEntity> items)
        {
            var data = new CheckLogEntity()
            {
                OrgUID = model.OrgUID,
                UserUID = model.UserUID,
                DeviceUID = model.DeviceUID,
                StatusOK = items.All(x => x.StatusOK > 0).ToBoolInt(),
                Tips = new List<string>() { }
            }.InitSelf("cklg");

            //汇总所有提示
            items.ForEach(x => data.Tips.AddWhenNotEmpty(x.Tips));

            if (!data.IsValid(out var msg))
            {
                throw new MsgException(msg);
            }

            data.DeviceModel = await this._deviceRepo.GetFirstAsync(x => x.UID == data.DeviceUID);
            if (data.DeviceModel == null)
            {
                throw new MsgException("设备不存在");
            }

            return await Task.FromResult(data);
        }

        private async Task<List<CheckLogItemEntity>> PrepareCheckLogItem(DeviceInputData model)
        {
            var param_uids = model.Data.Select(x => x.ParamUID).ToList();
            var params_list = await this._paramRepo.GetListAsync(x => param_uids.Contains(x.UID));

            var list = new List<CheckLogItemEntity>();
            foreach (var m in model.Data)
            {
                var p = params_list.FirstOrDefault(x => x.UID == m.ParamUID) ??
                    throw new MsgException("参数错误，部分设备参数不存在");
                var value = m.ValueJson;

                var entity = new CheckLogItemEntity()
                {
                    DeviceUID = p.DeviceUID,
                    ParameterUID = p.UID,
                    ParameterName = p.ParameterName,
                    Rule = p.Rule,
                    InputDataJson = value,
                    InputType = p.InputType,
                    Tips = new List<string>()
                }.InitSelf("log-itm");

                if (p.InputType == (int)DeviceParameterTypeEnum.字符)
                {
                    var expression = p.Rule?.JsonToEntity<StringInputExpression>();
                    var input = value?.JsonToEntity<StringValue>();
                    if (expression == null || input == null)
                    {
                        throw new MsgException("参数错误");
                    }
                    var res = expression.ValidInputs(input.Value);

                    entity.StatusOK = res.OK.ToBoolInt();
                    entity.Tips.AddWhenNotEmpty(res.Tips);
                }
                else if (p.InputType == (int)DeviceParameterTypeEnum.布尔)
                {
                    var expression = p.Rule?.JsonToEntity<BoolInputExpression>();
                    var input = value?.JsonToEntity<BoolValue>();
                    if (expression == null || input == null)
                    {
                        throw new MsgException("参数错误");
                    }
                    var res = expression.ValidInputs(input.Value);

                    entity.StatusOK = res.OK.ToBoolInt();
                    entity.Tips.AddWhenNotEmpty(res.Tips);
                }
                else if (p.InputType == (int)DeviceParameterTypeEnum.数值)
                {
                    var expression = p.Rule?.JsonToEntity<NumberInputExpression>();
                    var input = value?.JsonToEntity<NumberValue>();
                    if (expression == null || input == null)
                    {
                        throw new MsgException("参数错误");
                    }
                    var res = expression.ValidInputs(input.Value);

                    entity.StatusOK = res.OK.ToBoolInt();
                    entity.Tips.AddWhenNotEmpty(res.Tips);
                }
                else if (p.InputType == (int)DeviceParameterTypeEnum.选项)
                {
                    var expression = p.Rule?.JsonToEntity<SelectInputExpression>();
                    var input = value?.JsonToEntity<SelectValue>();
                    if (expression == null || input == null)
                    {
                        throw new MsgException("参数错误");
                    }
                    var res = expression.ValidInputs(input.Value);

                    entity.StatusOK = res.OK.ToBoolInt();
                    entity.Tips.AddWhenNotEmpty(res.Tips);
                }
                else
                {
                    throw new MsgException("参数类型错误");
                }

                list.Add(entity);
            }
            return list;
        }

        private async Task<IssueEntity> PrepareIssue(CheckLogEntity model, List<CheckLogItemEntity> items)
        {
            if (model.StatusOK > 0)
            {
                return null;
            }

            var data = new IssueEntity()
            {
                Title = $"[system]设备{model.DeviceModel.Name}存在问题",
                Content = string.Join("\n\n", model.Tips),
                ContentMarkdown = string.Join("\n\n", model.Tips),
                OrgUID = model.OrgUID,
                UserUID = model.UserUID,
                DeviceUID = model.DeviceUID,
                AssignedUserUID = model.UserUID,
                IsClosed = (int)YesOrNoEnum.否,
                Start = DateTime.Now,
            }.InitSelf("isu");

            return await Task.FromResult(data);
        }

        public async Task<_<List<CheckInputDataResult>>> SubmitCheckLog(DeviceInputData model)
        {
            var data = new _<List<CheckInputDataResult>>();

            try
            {
                //检查输入
                this.CheckInputData(model);

                //拼接数据
                var items = await this.PrepareCheckLogItem(model);
                var log = await this.PrepareCheckLog(model, items);

                var issue = await this.PrepareIssue(log, items);

                //数据关联
                foreach (var m in items)
                {
                    m.CheckUID = log.UID;
                }

                //保存数据
                var count = await this._logRepo.PrepareSessionAsync(async db =>
                {
                    db.Set<CheckLogEntity>().Add(log);
                    db.Set<CheckLogItemEntity>().AddRange(items);
                    if (issue != null)
                    {
                        db.Set<IssueEntity>().Add(issue);
                    }

                    return await db.SaveChangesAsync();
                });

                //获取返回
                var res = items.Select(x => new CheckInputDataResult()
                {
                    ParamUID = x.ParameterUID,
                    ParamName = x.ParameterName,
                    StatusOk = x.StatusOK.ToBool(),
                    Tips = x.Tips
                }).ToList();
                data.SetSuccessData(res);
                return data;
            }
            catch (MsgException e)
            {
                data.SetErrorMsg(e.Message);
                return data;
            }
            catch (Exception e)
            {
                throw new Exception($"提交点检数据错误：{model?.ToJson()}", e);
            }
        }

        public async Task<List<LogTimeGroupByDevice>> QueryLastCheckLogTime(string org_uid, string[] device_uids, DateTime after)
        {
            return await this._logRepo.PrepareIQueryableAsync(async query =>
            {
                query = query.Where(x => x.OrgUID == org_uid && device_uids.Contains(x.DeviceUID));
                query = query.Where(x => x.CreateTime > after);

                var data = await query
                .GroupBy(x => x.DeviceUID)
                .Select(x => new
                {
                    x.Key,
                    LogTime = x.Max(m => m.CreateTime),
                }).ToListAsync();

                return data.Select(x => new LogTimeGroupByDevice()
                {
                    DeviceUID = x.Key,
                    LogTime = x.LogTime
                }).ToList();
            });
        }

        public async Task<List<CheckLogEntity>> QueryCheckLog(string org_uid, int count)
        {
            return await this._logRepo.PrepareSessionAsync(async db =>
            {
                var log_query = db.Set<CheckLogEntity>().AsNoTrackingQueryable();
                var device_query = db.Set<DeviceEntity>().AsNoTrackingQueryable();

                var data = await log_query.Where(x => x.OrgUID == org_uid)
                .OrderByDescending(x => x.IID).Take(count).ToListAsync();

                if (ValidateHelper.IsPlumpList(data))
                {
                    var user_uids = data.Select(x => x.UserUID).ToList();
                    var device_uids = data.Select(x => x.DeviceUID).ToList();

                    var users = await this._userRepo.GetListAsync(x => user_uids.Contains(x.UID));
                    var devices = await device_query.Where(x => device_uids.Contains(x.UID)).ToListAsync();

                    foreach (var m in data)
                    {
                        m.UserModel = users.Where(x => x.UID == m.UserUID).FirstOrDefault();
                        m.DeviceModel = devices.Where(x => x.UID == m.DeviceUID).FirstOrDefault();
                    }
                }

                return data;
            });
        }
    }
}
