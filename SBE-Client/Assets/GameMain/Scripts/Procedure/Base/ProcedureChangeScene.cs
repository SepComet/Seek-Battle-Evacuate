using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Event;
using SepCore.AsyncTask;
using SepCore.Definition;
using SepCore.Sound;
using SepCore.UI;
using SepCore.Utility;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace SepCore.Procedure
{
    public class ProcedureChangeScene : ProcedureBase
    {
        private const float FadeDuration = 0.35f;

        private int _nextSceneId = 0;
        private bool _isChangeSceneComplete = false;
        private int _backgroundMusicId = 0;
        private CancellationTokenSource _cts = null;
        private UniTaskCompletionSource<bool> _sceneLoadTcs = null;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            _isChangeSceneComplete = false;
            _cts = new CancellationTokenSource();
            _sceneLoadTcs = null;

            GameEntry.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameEntry.Event.Subscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            GameEntry.Event.Subscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
            GameEntry.Event.Subscribe(LoadSceneDependencyAssetEventArgs.EventId, OnLoadSceneDependencyAsset);

            _nextSceneId = procedureOwner.GetData<VarInt32>("NextSceneId");

            ChangeSceneProcessAsync(_cts.Token).Forget();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            _sceneLoadTcs = null;

            GameEntry.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameEntry.Event.Unsubscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            GameEntry.Event.Unsubscribe(LoadSceneUpdateEventArgs.EventId, OnLoadSceneUpdate);
            GameEntry.Event.Unsubscribe(LoadSceneDependencyAssetEventArgs.EventId, OnLoadSceneDependencyAsset);

            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (!_isChangeSceneComplete)
            {
                return;
            }

            SceneType sceneType = (SceneType)_nextSceneId;
            switch (sceneType)
            {
                case SceneType.Menu:
                    ChangeState<ProcedureMenu>(procedureOwner);
                    break;
                case SceneType.Main:
                    ChangeState<ProcedureMain>(procedureOwner);
                    break;
                default:
                    Log.Error($"Scene {sceneType.ToString()} don't configure a procedure");
                    break;
            }
        }

        private async UniTaskVoid ChangeSceneProcessAsync(CancellationToken cancellationToken)
        {
            FadeForm fadeForm = null;

            try
            {
                // 1. 获取或打开 FadeForm
                fadeForm = GameEntry.UI.GetUIForm(UIFormType.FadeForm) as FadeForm;
                if (fadeForm == null)
                {
                    UIForm uiForm = await GameEntry.UI.OpenUIFormAsync(UIFormType.FadeForm).AttachExternalCancellation(cancellationToken);
                    if (uiForm != null)
                    {
                        fadeForm = uiForm.Logic as FadeForm;
                    }
                }

                // 2. 切换场景前先执行 FadeForm 的淡入（画面渐黑遮盖屏幕，阻断输入）
                if (fadeForm != null)
                {
                    await fadeForm.FadeInAsync(FadeDuration, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 3. 淡入完毕，屏幕完全被遮盖，开始清理旧场景与实体，并加载新场景
                // 停止所有声音
                GameEntry.Sound.StopAllLoadingSounds();
                GameEntry.Sound.StopAllLoadedSounds();

                // 隐藏所有实体
                GameEntry.Entity.HideAllLoadingEntities();
                GameEntry.Entity.HideAllLoadedEntities();

                // 卸载所有旧场景
                string[] loadedSceneAssetNames = GameEntry.Scene.GetLoadedSceneAssetNames();
                for (int i = 0; i < loadedSceneAssetNames.Length; i++)
                {
                    GameEntry.Scene.UnloadScene(loadedSceneAssetNames[i]);
                }

                // 还原游戏速度
                GameEntry.Base.ResetNormalGameSpeed();

                SceneConfig sceneConfig = GameEntry.Luban.Get<SceneConfig>(_nextSceneId);
                if (sceneConfig == null)
                {
                    Log.Error("Can not load scene '{0}' from data table.", _nextSceneId.ToString());
                    if (fadeForm != null)
                    {
                        await fadeForm.FadeOutAsync(FadeDuration, cancellationToken);
                        GameEntry.UI.CloseUIForm(fadeForm);
                    }
                    _isChangeSceneComplete = true;
                    return;
                }

                _backgroundMusicId = sceneConfig.BackgroundMusicId;

                // 启动场景异步加载，并等待完成
                _sceneLoadTcs = new UniTaskCompletionSource<bool>();
                GameEntry.Scene.LoadScene(AssetUtility.GetSceneAsset(sceneConfig.AssetName), Constant.AssetPriority.SceneAsset, this);

                await _sceneLoadTcs.Task.AttachExternalCancellation(cancellationToken);

                // 4. 场景加载完毕后，播放背景音乐
                if (_backgroundMusicId > 0)
                {
                    GameEntry.Sound.PlayMusic(_backgroundMusicId);
                }

                // 5. 场景加载完毕后执行 FadeForm 的淡出（画面渐显呈现新场景）
                if (fadeForm != null)
                {
                    await fadeForm.FadeOutAsync(FadeDuration, cancellationToken);
                    GameEntry.UI.CloseUIForm(fadeForm);
                }

                // 6. 转场淡出全流程完成，通知 OnUpdate 执行流程状态切换
                _isChangeSceneComplete = true;
            }
            catch (OperationCanceledException)
            {
                Log.Info("[ProcedureChangeScene] Change scene process was canceled.");
            }
            catch (Exception ex)
            {
                Log.Error("[ProcedureChangeScene] Change scene failed with exception: {0}", ex.ToString());
                if (fadeForm != null)
                {
                    try
                    {
                        await fadeForm.FadeOutAsync(FadeDuration, CancellationToken.None);
                        GameEntry.UI.CloseUIForm(fadeForm);
                    }
                    catch
                    {
                        // 忽略异常处理阶段的二次异常
                    }
                }
                _isChangeSceneComplete = true;
            }
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            LoadSceneSuccessEventArgs ne = (LoadSceneSuccessEventArgs)e;
            if (ne.UserData != this)
            {
                return;
            }

            Log.Info("Load scene '{0}' OK.", ne.SceneAssetName);
            _sceneLoadTcs?.TrySetResult(true);
        }

        private void OnLoadSceneFailure(object sender, GameEventArgs e)
        {
            LoadSceneFailureEventArgs ne = (LoadSceneFailureEventArgs)e;
            if (ne.UserData != this)
            {
                return;
            }

            Log.Error("Load scene '{0}' failure, error message '{1}'.", ne.SceneAssetName, ne.ErrorMessage);
            _sceneLoadTcs?.TrySetException(new GameFramework.GameFrameworkException(
                GameFramework.Utility.Text.Format("Load scene '{0}' failure, error message '{1}'.", ne.SceneAssetName, ne.ErrorMessage)));
        }

        private void OnLoadSceneUpdate(object sender, GameEventArgs e)
        {
            LoadSceneUpdateEventArgs ne = (LoadSceneUpdateEventArgs)e;
            if (ne.UserData != this)
            {
                return;
            }

            Log.Info("Load scene '{0}' update, progress '{1}'.", ne.SceneAssetName, ne.Progress.ToString("P2"));
        }

        private void OnLoadSceneDependencyAsset(object sender, GameEventArgs e)
        {
            LoadSceneDependencyAssetEventArgs ne = (LoadSceneDependencyAssetEventArgs)e;
            if (ne.UserData != this)
            {
                return;
            }

            Log.Info("Load scene '{0}' dependency asset '{1}', count '{2}/{3}'.", ne.SceneAssetName,
                ne.DependencyAssetName, ne.LoadedCount.ToString(), ne.TotalCount.ToString());
        }
    }
}
