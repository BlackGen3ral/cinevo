namespace Cinevo;
internal sealed class EpisodeCountdown
{
    private DateTime? deadline;
    internal bool Cancelled {get;private set;}
    internal void Begin(DateTime now){if(!Cancelled&&deadline==null)deadline=now.AddSeconds(10);}
    internal int Remaining(DateTime now)=>deadline==null?0:Math.Max(0,(int)Math.Ceiling((deadline.Value-now).TotalSeconds));
    internal bool Due(DateTime now)=>deadline!=null&&!Cancelled&&Remaining(now)==0;
    internal void Cancel(){Cancelled=true;deadline=null;}
    internal void Reset(){Cancelled=false;deadline=null;}
}
internal static class HomeFeed
{
    internal static IEnumerable<Movie> Returning(IEnumerable<Movie> movies,string profile,string active)=>movies.Where(m=>!Viewers.Get(m,profile,active).Watched&&Viewers.Get(m,profile,active).Resume>1000).OrderByDescending(m=>m.Added).Take(12);
    internal static IEnumerable<Movie> Next(IEnumerable<Movie> movies,string profile,string active)=>movies.Where(m=>ImportedTitle.Describe(m).Type=="series").GroupBy(SeriesCenter.Key)
        .Where(g=>g.Any(m=>Viewers.Get(m,profile,active).Watched||Viewers.Get(m,profile,active).Resume>1000))
        .Select(g=>SeriesCenter.Continue(g,profile,active)).Where(m=>m!=null&&!Viewers.Get(m,profile,active).Watched).Select(m=>m!).Take(12);
}
