using Sora.Attributes.Command;
using Sora.Entities.Info;
using Sora.Enumeration;
using Sora.Enumeration.EventParamsType;
using Sora.EventArgs.SoraEvent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sora.Attributes;
using Sora.OnebotInterface;
using YukariToolBox.FormatLog;
using YukariToolBox.Helpers;

namespace Sora.Command
{
    /// <summary>
    /// 特性指令管理器
    /// </summary>
    public class CommandManager
    {
        #region 属性

        /// <summary>
        /// 指令服务正常运行标识
        /// </summary>
        public bool ServiceIsRunning { get; private set; }

        #endregion

        #region 私有字段

        private readonly List<CommandInfo> _groupCommands = new();

        private readonly List<CommandInfo> _privateCommands = new();

        private readonly Dictionary<Type, dynamic> _instanceDict = new();

        private readonly bool _enableSoraCommandManager;

        #endregion

        #region 构造方法

        internal CommandManager(bool enableSoraCommandManager)
        {
            ServiceIsRunning          = false;
            _enableSoraCommandManager = enableSoraCommandManager;
        }

        #endregion

        #region 公有管理方法

        /// <summary>
        /// 自动注册所有指令
        /// </summary>
        /// <param name="assembly">包含指令的程序集</param>
        [Reviewed("XiaoHe321", "2021-03-28 20:45")]
        public void MappingCommands(Assembly assembly)
        {
            //检查使能
            if (!_enableSoraCommandManager) return;
            if (assembly == null) return;

            //查找所有的指令集
            var cmdGroups = assembly.GetExportedTypes()
                                    //获取指令组
                                    .Where(type => type.IsDefined(typeof(CommandGroup), false) && type.IsClass)
                                    .Select(type => (type,
                                                     type.GetMethods()
                                                         .Where(method => method.CheckMethodLegality())
                                                         .ToArray())
                                           )
                                    .ToDictionary(methods => methods.type, methods => methods.Item2.ToArray());

            //生成指令信息
            foreach (var (classType, methodInfos) in cmdGroups)
            {
                foreach (MethodInfo methodInfo in methodInfos)
                {
                    switch (GenerateCommandInfo(methodInfo, classType, out var commandInfo))
                    {
                        case GroupCommand:
                            if (_groupCommands.AddOrExist(commandInfo))
                                Log.Debug("Command", $"Registered group command [{methodInfo.Name}]");
                            break;
                        case PrivateCommand:
                            if (_privateCommands.AddOrExist(commandInfo))
                                Log.Debug("Command", $"Registered private command [{methodInfo.Name}]");
                            break;
                        default:
                            Log.Warning("Command", "未知的指令类型");
                            break;
                    }
                }
            }

            ServiceIsRunning = true;
        }

        /// <summary>
        /// 处理聊天指令的单次处理器
        /// </summary>
        /// <param name="eventArgs">事件参数</param>
        /// <returns>是否继续处理接下来的消息</returns>
        internal bool CommandAdapter(dynamic eventArgs)
        {
            //检查使能
            if (!_enableSoraCommandManager) return true;

            #region 信号量消息处理

            //处理消息段
            Dictionary<Guid, StaticVariable.WaitingInfo> waitingCommand;
            switch (eventArgs)
            {
                case GroupMessageEventArgs groupMessageEvent:
                {
                    //注意可能匹配到多个的情况，下同
                    waitingCommand = StaticVariable.WaitingDict
                                                   .Where(command =>
                                                              //判断来自同一个连接
                                                              command.Value.ConnectionId ==
                                                              groupMessageEvent.SoraApi.ConnectionGuid
                                                              //判断来着同一个群
                                                           && command.Value.Source.g == groupMessageEvent.SourceGroup
                                                              //判断来自同一人
                                                           && command.Value.Source.u == groupMessageEvent.Sender
                                                              //匹配
                                                           && command.Value.CommandExpressions.Any(regex =>
                                                                  Regex
                                                                      .IsMatch(groupMessageEvent.Message.RawText,
                                                                               regex,
                                                                               RegexOptions.Compiled |
                                                                               command.Value
                                                                                   .RegexOptions)))
                                                   .ToDictionary(i => i.Key, i => i.Value);
                    break;
                }
                case PrivateMessageEventArgs privateMessageEvent:
                {
                    waitingCommand = StaticVariable.WaitingDict
                                                   .Where(command =>
                                                              //判断来自同一个连接
                                                              command.Value.ConnectionId ==
                                                              privateMessageEvent.SoraApi.ConnectionGuid
                                                              //判断来自同一人
                                                           && command.Value.Source.u == privateMessageEvent.Sender
                                                              //匹配指令
                                                           && command.Value.CommandExpressions.Any(regex =>
                                                                  Regex
                                                                      .IsMatch(privateMessageEvent.Message.RawText,
                                                                               regex,
                                                                               RegexOptions.Compiled |
                                                                               command.Value
                                                                                   .RegexOptions)))
                                                   .ToDictionary(i => i.Key, i => i.Value);
                    break;
                }
                default:
                    Log.Error("CommandAdapter", "cannot parse eventArgs");
                    return true;
            }

            foreach (var (key, _) in waitingCommand)
            {
                //更新等待列表，设置为当前的eventArgs
                var oldInfo = StaticVariable.WaitingDict[key];
                var newInfo = oldInfo;
                newInfo.EventArgs = eventArgs;
                StaticVariable.WaitingDict.TryUpdate(key, newInfo, oldInfo);
                StaticVariable.WaitingDict[key].Semaphore.Set();
            }

            //当前流程已经处理过wait command了。不再继续处理普通command，否则会一次发两条消息，普通消息留到下一次处理
            if (waitingCommand.Count != 0) return false;

            #endregion

            #region 常规指令处理

            List<CommandInfo> matchedCommand;
            switch (eventArgs)
            {
                case GroupMessageEventArgs groupMessageEvent:
                {
                    //注意可能匹配到多个的情况，下同
                    matchedCommand =
                        _groupCommands.Where(command => command.Regex.Any(regex =>
                                                                              Regex
                                                                                  .IsMatch(groupMessageEvent.Message.RawText,
                                                                                      regex,
                                                                                      RegexOptions.Compiled |
                                                                                      command.RegexOptions)
                                                                           && command.MethodInfo != null))
                                      .OrderByDescending(p => p.Priority)
                                      .ToList();
                    break;
                }
                case PrivateMessageEventArgs privateMessageEvent:
                {
                    matchedCommand =
                        _privateCommands.Where(command => command.Regex.Any(regex =>
                                                                                Regex
                                                                                    .IsMatch(privateMessageEvent.Message.RawText,
                                                                                        regex,
                                                                                        RegexOptions.Compiled |
                                                                                        command.RegexOptions)
                                                                             && command.MethodInfo != null))
                                        .OrderByDescending(p => p.Priority)
                                        .ToList();

                    break;
                }
                default:
                    Log.Error("CommandAdapter", "cannot parse eventArgs");
                    return true;
            }

            //在没有匹配到指令时直接跳转至Event触发
            if (matchedCommand.Count == 0) return true;

            //遍历匹配到的每个命令
            foreach (var commandInfo in matchedCommand)
            {
                //若是群，则判断权限
                if (eventArgs is GroupMessageEventArgs groupEventArgs)
                {
                    if (groupEventArgs.SenderInfo.Role < (commandInfo.PermissonType ?? MemberRoleType.Member))
                    {
                        Log.Warning("CommandAdapter",
                                    $"成员{groupEventArgs.SenderInfo.UserId}正在尝试执行指令{commandInfo.MethodInfo.Name}");

                        //权限不足，跳过本命令执行
                        continue;
                    }
                }

                Log.Debug("CommandAdapter",
                          $"trigger command [{commandInfo.MethodInfo.ReflectedType?.FullName}.{commandInfo.MethodInfo.Name}]");
                Log.Info("CommandAdapter", $"触发指令[{commandInfo.MethodInfo.Name}]");

                try
                {
                    //尝试执行指令并判断异步方法
                    var returnParameterType = commandInfo.MethodInfo.ReturnParameter.ParameterType;
                    //执行指令方法
                    if (returnParameterType == typeof(Task) || returnParameterType == typeof(ValueTask))
                    {
                        Task asyncTask = Task.Run(async () =>
                                                  {
                                                      await ((dynamic) commandInfo.MethodInfo
                                                          .Invoke(commandInfo.InstanceType == null ? null : _instanceDict[commandInfo.InstanceType],
                                                                  new[] {eventArgs}))!;
                                                  });
                        asyncTask.Wait();
                    }
                    else
                        commandInfo.MethodInfo
                                   .Invoke(commandInfo.InstanceType == null ? null : _instanceDict[commandInfo.InstanceType],
                                           new[] {eventArgs});

                    return ((BaseSoraEventArgs) eventArgs).IsContinueEventChain;
                }
                catch (Exception e)
                {
                    Log.Error("CommandAdapter", Log.ErrorLogBuilder(e));

                    //描述不为空，才需要打印错误信息
                    if (!string.IsNullOrEmpty(commandInfo.Desc))
                    {
                        switch (eventArgs)
                        {
                            case GroupMessageEventArgs groupMessageEvent:
                            {
                                Task.Run(async () => { await groupMessageEvent.Reply($"指令执行错误\n{commandInfo.Desc}"); });
                                break;
                            }
                            case PrivateMessageEventArgs privateMessageEvent:
                            {
                                Task.Run(async () =>
                                         {
                                             await privateMessageEvent.Reply($"指令执行错误\n{commandInfo.Desc}");
                                         });
                                break;
                            }
                        }
                    }
                }
            }

            #endregion

            return true;
        }

        /// <summary>
        /// 获取已注册过的实例
        /// </summary>
        /// <param name="instance">实例</param>
        /// <typeparam name="T">Type</typeparam>
        /// <returns>获取是否成功</returns>
        [Reviewed("XiaoHe321", "2021-03-28 20:45")]
        public bool GetInstance<T>(out T instance)
        {
            if (_instanceDict.Any(type => type.Key == typeof(T)) && _instanceDict[typeof(T)] is T outVal)
            {
                instance = outVal;
                return true;
            }

            instance = default;
            return false;
        }

        #endregion

        #region 私有管理方法

        /// <summary>
        /// 生成连续对话上下文
        /// </summary>
        /// <param name="sourceUid">消息源UID</param>
        /// <param name="sourceGroup">消息源GID</param>
        /// <param name="cmdExps">指令表达式</param>
        /// <param name="matchType">匹配类型</param>
        /// <param name="regexOptions">正则匹配选项</param>
        /// <param name="connectionId">连接标识</param>
        /// <exception cref="NullReferenceException">表达式为空时抛出异常</exception>
        [Reviewed("XiaoHe321", "2021-03-28 20:45")]
        internal static StaticVariable.WaitingInfo GenWaitingCommandInfo(
            long sourceUid, long sourceGroup, string[] cmdExps,
            MatchType matchType, RegexOptions regexOptions, Guid connectionId)
        {
            if (cmdExps == null || cmdExps.Length == 0) throw new NullReferenceException("cmdExps is empty");
            var matchExp = matchType switch
            {
                MatchType.Full => cmdExps
                                  .Select(command => $"^{command}$")
                                  .ToArray(),
                MatchType.Regex => cmdExps,
                MatchType.KeyWord => cmdExps
                                     .Select(command => $"[{command}]+")
                                     .ToArray(),
                _ => throw new NotSupportedException("unknown matchtype")
            };

            return new StaticVariable.WaitingInfo(semaphore: new AutoResetEvent(false),
                                                  commandExpressions: matchExp,
                                                  connectionId: connectionId,
                                                  source: (sourceUid, sourceGroup),
                                                  regexOptions: regexOptions);
        }

        /// <summary>
        /// 生成指令信息
        /// </summary>
        /// <param name="method">指令method</param>
        /// <param name="classType">所在实例类型</param>
        /// <param name="commandInfo">指令信息</param>
        [Reviewed("XiaoHe321", "2021-03-28 20:45")]
        private Attribute GenerateCommandInfo(MethodInfo method, Type classType, out CommandInfo commandInfo)
        {
            //获取指令属性
            var commandAttr =
                method.GetCustomAttribute(typeof(GroupCommand)) ??
                method.GetCustomAttribute(typeof(PrivateCommand)) ??
                throw new NullReferenceException("command attribute is null with unknown reason");

            Log.Debug("Command", $"Registering command [{method.Name}]");

            //处理指令匹配类型
            var match = (commandAttr as Attributes.Command.Command)?.MatchType ?? MatchType.Full;
            //处理表达式
            var matchExp = match switch
            {
                MatchType.Full => (commandAttr as Attributes.Command.Command)?.CommandExpressions
                                                                             .Select(command => $"^{command}$")
                                                                             .ToArray(),
                MatchType.Regex => (commandAttr as Attributes.Command.Command)?.CommandExpressions,
                MatchType.KeyWord => (commandAttr as Attributes.Command.Command)?.CommandExpressions
                    .Select(command => $"[{command}]+")
                    .ToArray(),
                _ => null
            };
            //若无匹配表达式，则创建一个空白的命令信息
            if (matchExp == null)
            {
                commandInfo = ObjectHelper.CreateInstance<CommandInfo>();
                return null;
            }

            //检查和创建实例
            //若创建实例失败且方法不是静态的，则返回空白命令信息
            if (!method.IsStatic && !CheckAndCreateInstance(classType))
            {
                commandInfo = ObjectHelper.CreateInstance<CommandInfo>();
                return null;
            }

            //创建指令信息
            commandInfo = new CommandInfo((commandAttr as Attributes.Command.Command)?.Description,
                                          matchExp,
                                          classType.Name,
                                          method,
                                          (commandAttr as GroupCommand)?.PermissionLevel,
                                          (commandAttr as Attributes.Command.Command)?.Priority ?? 0,
                                          (commandAttr as Attributes.Command.Command)?.RegexOptions ??
                                          RegexOptions.None,
                                          method.IsStatic ? null : classType);

            return commandAttr;
        }

        /// <summary>
        /// 检查实例的存在和生成
        /// </summary>
        [Reviewed("XiaoHe321", "2021-03-16 21:07")]
        private bool CheckAndCreateInstance(Type classType)
        {
            //获取类属性
            if (!classType?.IsClass ?? true)
            {
                Log.Error("Command", "method reflected objcet is not a class");
                return false;
            }

            //检查是否已创建过实例
            if (_instanceDict.Any(ins => ins.Key == classType)) return true;

            try
            {
                //创建实例
                var instance = classType.CreateInstance();

                //添加实例
                _instanceDict
                    .Add(classType ?? throw new ArgumentNullException(nameof(classType), "get null class type"),
                         instance);
            }
            catch (Exception e)
            {
                Log.Error("Command", $"cannot create instance with error:{Log.ErrorLogBuilder(e)}");
                return false;
            }

            return true;
        }

        #endregion
    }
}