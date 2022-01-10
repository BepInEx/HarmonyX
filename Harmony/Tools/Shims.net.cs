#if NET35 || NET45
namespace System
{
	internal struct ValueTuple<T1, T2>
	{
		public T1 Item1;
		public T2 Item2;

		public ValueTuple(T1 first, T2 second)
		{
			Item1 = first;
			Item2 = second;
		}
	}

	internal struct ValueTuple<T1, T2, T3>
	{
		public T1 Item1;
		public T2 Item2;
		public T3 Item3;

		public ValueTuple(T1 first, T2 second, T3 third)
		{
			Item1 = first;
			Item2 = second;
			Item3 = third;
		}
	}
}
#endif
