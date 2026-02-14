namespace Fantasy
{
    internal interface ISceneUpdate
    {
        void Update();
    }

    internal sealed class EmptySceneUpdate : ISceneUpdate
    {
        public void Update()
        {

        }
    }

#if FANTASY_UNITY
    internal interface ISceneLateUpdate
    {
        void LateUpdate();
    }

    internal sealed class EmptySceneLateUpdate : ISceneLateUpdate
    {
        public void LateUpdate()
        {
        
        }
    }
#endif

}