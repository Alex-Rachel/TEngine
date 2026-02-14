using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    [CreateAssetMenu(menuName = "TEngine/ProcedureSetting", fileName = "ProcedureSetting")]
    public sealed class ProcedureSetting : ScriptableObject
    {
        private IProcedureModule _procedureModule = null;
        private ProcedureBase _entranceProcedure = null;

        [SerializeField]
        private string[] availableProcedureTypeNames = null;

        [SerializeField]
        private string entranceProcedureTypeName = null;

        /// <summary>
        /// 获取当前流程。
        /// </summary>
        public ProcedureBase CurrentProcedure
        {
            get
            {
                if (_procedureModule == null)
                {
                    return null;
                }

                return _procedureModule.CurrentProcedure;
            }
        }

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public float CurrentProcedureTime
        {
            get
            {
                if (_procedureModule == null)
                {
                    return 0f;
                }

                return _procedureModule.CurrentProcedureTime;
            }
        }

        /// <summary>
        /// 启动流程。
        /// </summary>
        public async UniTaskVoid StartProcedure()
        {
            if (_procedureModule == null)
            {
                _procedureModule = ModuleSystem.GetModule<IProcedureModule>();
            }

            if (_procedureModule == null)
            {
                Log.Fatal("Procedure manager is invalid.");
                return;
            }

            ProcedureBase[] procedures = new ProcedureBase[availableProcedureTypeNames.Length];
            for (int i = 0; i < availableProcedureTypeNames.Length; i++)
            {
                Type procedureType = Utility.Assembly.GetType(availableProcedureTypeNames[i]);
                if (procedureType == null)
                {
                    Log.Error("Can not find procedure type '{0}'.", availableProcedureTypeNames[i]);
                    return;
                }

                procedures[i] = (ProcedureBase)Activator.CreateInstance(procedureType);
                if (procedures[i] == null)
                {
                    Log.Error("Can not create procedure instance '{0}'.", availableProcedureTypeNames[i]);
                    return;
                }

                if (entranceProcedureTypeName == availableProcedureTypeNames[i])
                {
                    _entranceProcedure = procedures[i];
                }
            }

            if (_entranceProcedure == null)
            {
                Log.Error("Entrance procedure is invalid.");
                return;
            }

            _procedureModule.Initialize(ModuleSystem.GetModule<IFsmModule>(), procedures);

            await UniTask.Yield();

            _procedureModule.StartProcedure(_entranceProcedure.GetType());
        }
    }
}