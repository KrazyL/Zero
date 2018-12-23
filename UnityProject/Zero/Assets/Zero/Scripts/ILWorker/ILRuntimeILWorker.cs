﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.EventSystems;

namespace Zero
{
    public class ILRuntimeILWorker : BaseILWorker
    {
        static private List<BaseILRuntimeGenerics> _rgList = new List<BaseILRuntimeGenerics>();

        /// <summary>
        /// 注册ILRuntime的适配器注册类
        /// </summary>
        /// <param name="rg"></param>
        internal static void RegisterILRuntimeGenerics(BaseILRuntimeGenerics rg)
        {
            _rgList.Add(rg);
        }

        //AppDomain是ILRuntime的入口，最好是在一个单例类中保存，整个游戏全局就一个，这里为了示例方便，每个例子里面都单独做了一个
        //大家在正式项目中请全局只创建一个AppDomain
        ILRuntime.Runtime.Enviorment.AppDomain _appdomain = null;

        public ILRuntimeILWorker(byte[] dllBytes, string libDir, string libName, bool isDebug, bool isNeedPdbFile)
        {
            Log.CI(Log.COLOR_ORANGE, "外部程序集执行方式：[ILRuntime]");

            //首先实例化ILRuntime的AppDomain，AppDomain是一个应用程序域，每个AppDomain都是一个独立的沙盒
            _appdomain = new ILRuntime.Runtime.Enviorment.AppDomain();

            if (isDebug)
            {
                //启动调试监听
                _appdomain.DebugService.StartDebugService(56000);
            }

            if (isNeedPdbFile)
            {
                string pdbPath = Path.Combine(libDir, libName + ".pdb");
                byte[] pdbBytes = File.ReadAllBytes(pdbPath);
                using (MemoryStream fs = new MemoryStream(dllBytes))
                {
                    using (MemoryStream p = new MemoryStream(pdbBytes))
                    {
                        _appdomain.LoadAssembly(fs, p, new Mono.Cecil.Pdb.PdbReaderProvider());
                    }
                }
            }
            else
            {
                using (MemoryStream fs = new MemoryStream(dllBytes))
                {
                    _appdomain.LoadAssembly(fs);
                }
            }

            InitializeILRuntime();
        }

        void InitializeILRuntime()
        {
            var appdomain = _appdomain;

            //注册LitJson
            LitJson.JsonMapper.RegisterILRuntimeCLRRedirection(appdomain);

            //注册CLR绑定
            ILRuntime.Runtime.Generated.CLRBindings.Initialize(appdomain);

            //使用Couroutine时，C#编译器会自动生成一个实现了IEnumerator，IEnumerator<object>，IDisposable接口的类，因为这是跨域继承，所以需要写CrossBindAdapter
            appdomain.RegisterCrossBindingAdaptor(new CoroutineAdapter());

            #region 这里做一些Zero依赖的的ILRuntime的注册
            appdomain.DelegateManager.RegisterMethodDelegate<float>();
            appdomain.DelegateManager.RegisterMethodDelegate<PointerEventData>();
            appdomain.DelegateManager.RegisterMethodDelegate<AxisEventData>();
            appdomain.DelegateManager.RegisterMethodDelegate<UnityEngine.Object>();
            appdomain.DelegateManager.RegisterMethodDelegate<UnityEngine.Collider2D>();
            appdomain.DelegateManager.RegisterMethodDelegate<System.String, System.String>();

            appdomain.DelegateManager.RegisterMethodDelegate<System.Object, System.Net.DownloadProgressChangedEventArgs>();
            appdomain.DelegateManager.RegisterDelegateConvertor<System.Net.DownloadProgressChangedEventHandler>((act) =>
            {
                return new System.Net.DownloadProgressChangedEventHandler((sender, e) =>
                {
                    ((Action<System.Object, System.Net.DownloadProgressChangedEventArgs>)act)(sender, e);
                });
            });

            appdomain.DelegateManager.RegisterMethodDelegate<System.Object, System.ComponentModel.AsyncCompletedEventArgs>();
            appdomain.DelegateManager.RegisterDelegateConvertor<System.ComponentModel.AsyncCompletedEventHandler>((act) =>
            {
                return new System.ComponentModel.AsyncCompletedEventHandler((sender, e) =>
                {
                    ((Action<System.Object, System.ComponentModel.AsyncCompletedEventArgs>)act)(sender, e);
                });
            });

            appdomain.DelegateManager.RegisterDelegateConvertor<System.Threading.ThreadStart>((act) =>
            {
                return new System.Threading.ThreadStart(() =>
                {
                    ((Action)act)();
                });
            });

            appdomain.DelegateManager.RegisterDelegateConvertor<UnityEngine.Events.UnityAction>((act) =>
            {
                return new UnityEngine.Events.UnityAction(() =>
                {
                    ((Action)act)();
                });
            });
            #endregion

            //开发者自己的适配器注册类
            foreach (var rg in _rgList)
            {
                rg.Register(appdomain);
            }
        }

        public override void Invoke(string clsName, string methodName)
        {
            _appdomain.Invoke(clsName, methodName, null, null);
        }
    }
}