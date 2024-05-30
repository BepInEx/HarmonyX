using System;
using System.Linq.Expressions;
using System.Reflection;

namespace HarmonyLib
{
	/// <summary>A helper class to retrieve reflection info for non-private methods</summary>
	///
	public static class SymbolExtensions
	{
		/// <summary>Given a lambda expression that calls a method, returns the method info</summary>
		/// <param name="expression">The lambda expression using the method</param>
		/// <returns>The method in the lambda expression</returns>
		///
		public static MethodInfo GetMethodInfo(Expression<Action> expression) => GetMethodInfo((LambdaExpression)expression);

		/// <summary>Given a lambda expression that calls a method, returns the method info</summary>
		/// <typeparam name="T">The generic type</typeparam>
		/// <param name="expression">The lambda expression using the method</param>
		/// <returns>The method in the lambda expression</returns>
		///
		public static MethodInfo GetMethodInfo<T>(Expression<Action<T>> expression) => GetMethodInfo((LambdaExpression)expression);

		/// <summary>Given a lambda expression that calls a method, returns the method info</summary>
		/// <typeparam name="T">The generic type</typeparam>
		/// <typeparam name="TResult">The generic result type</typeparam>
		/// <param name="expression">The lambda expression using the method</param>
		/// <returns>The method in the lambda expression</returns>
		///
		public static MethodInfo GetMethodInfo<T, TResult>(Expression<Func<T, TResult>> expression) => GetMethodInfo((LambdaExpression)expression);

		/// <summary>Given a lambda expression that calls a method, returns the method info</summary>
		/// <param name="expression">The lambda expression using the method</param>
		/// <returns>The method in the lambda expression</returns>
		///
		public static MethodInfo GetMethodInfo(LambdaExpression expression)
		{
			var outermostExpression = expression.Body as MethodCallExpression;

			if (outermostExpression is null)
			{
				if (expression.Body is UnaryExpression ue && ue.Operand is MethodCallExpression me && me.Object is System.Linq.Expressions.ConstantExpression ce && ce.Value is MethodInfo mi)
					return mi;
				throw new ArgumentException("Invalid Expression. Expression should consist of a Method call only.");
			}

			var method = outermostExpression.Method;
			if (method is null)
				throw new Exception($"Cannot find method for expression {expression}");

			return method;
		}
	}
}
