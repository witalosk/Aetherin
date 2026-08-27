namespace Aetherin
{
    public interface IParams { }
    
    public interface IParamsTarget
    {
        IParams Params { get; }
    }
}
