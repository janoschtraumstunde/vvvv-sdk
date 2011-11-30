﻿
using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using VVVV.Hosting.Streams;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils;
using VVVV.Utils.Streams;

namespace StreamTests
{
	[TestFixture]
	public class UnmanagedStreamTests
	{
		private static double[] FDoubleArray;
		private static GCHandle FDoubleArrayHandle;
		private static int FDoubleArrayBegin = 4;
		private static int FDoubleArrayEnd = 4;
		
		private static Tuple<IntPtr, int> GetUnmanagedArray()
		{
			if (FDoubleArrayHandle.IsAllocated)
				FDoubleArrayHandle.Free();

			FDoubleArrayHandle = GCHandle.Alloc(FDoubleArray, GCHandleType.Pinned);
			return Tuple.Create(FDoubleArrayHandle.AddrOfPinnedObject() + sizeof(double) * FDoubleArrayBegin, FDoubleArray.Length - (FDoubleArrayBegin + FDoubleArrayEnd));
		}
		
		private static IntPtr ResizeUnmanagedArray(int newLength)
		{
			if (FDoubleArrayHandle.IsAllocated)
				FDoubleArrayHandle.Free();
			
			Array.Resize(ref FDoubleArray, newLength + FDoubleArrayBegin + FDoubleArrayEnd);
			FDoubleArray.Fill(0, FDoubleArrayBegin, double.MinValue);
			FDoubleArray.Fill(FDoubleArray.Length - FDoubleArrayEnd, FDoubleArrayEnd, double.MaxValue);
			FDoubleArrayHandle = GCHandle.Alloc(FDoubleArray, GCHandleType.Pinned);
			return FDoubleArrayHandle.AddrOfPinnedObject() + sizeof(double) * FDoubleArrayBegin;
		}
		
		private static bool Validate()
		{
			return true;
		}
		
		private void CheckArrayBoundaries()
		{
			for (int i = 0; i < FDoubleArrayBegin; i++)
				Assert.AreEqual(double.MinValue, FDoubleArray[i]);
			for (int i = FDoubleArray.Length - FDoubleArrayEnd; i < FDoubleArray.Length; i++)
				Assert.AreEqual(double.MaxValue, FDoubleArray[i]);
		}
		
		[SetUp]
		public void SetUp()
		{
			FDoubleArray = new double[16];
//			FDoubleArray.Init(1.0);
			for (int i = FDoubleArrayBegin; i < FDoubleArray.Length - FDoubleArrayEnd; i++)
				FDoubleArray[i] = i - FDoubleArrayBegin;
			FDoubleArray.Fill(0, FDoubleArrayBegin, double.MinValue);
			FDoubleArray.Fill(FDoubleArray.Length - FDoubleArrayEnd, FDoubleArrayEnd, double.MaxValue);
		}
		
		[Test]
		public void TestEos()
		{
			var stream = UnmanagedInStream.Create<double>(GetUnmanagedArray, Validate);
			using (var reader = stream.GetReader())
			{
				Assert.IsTrue(reader.Eos);
			}
			
			stream.Sync();
			
			using (var reader = stream.GetReader())
			{
				Assert.IsFalse(reader.Eos);
			}
		}
		
		[Test]
		public void TestRead()
		{
			var stream = UnmanagedInStream.Create<double>(GetUnmanagedArray, Validate);
			stream.Sync();
			
			using (var reader = stream.GetReader())
			{
				for (int stepSize = 1; stepSize < FDoubleArray.Length; stepSize++)
				{
					double s = 0.0;
					while (!reader.Eos)
					{
						double v = reader.Read(stepSize);
						Assert.AreEqual(s, v);
						s += stepSize;
					}
					CheckArrayBoundaries();
					reader.Reset();
				}
			}
		}
		
		[Test]
		public void TestBufferedRead()
		{
			var stream = UnmanagedInStream.Create<double>(GetUnmanagedArray, Validate);
			
			int startIndex = 2;
			int length = 2;
			
			using (var reader = stream.GetReader())
			{
				for (int stepSize = 1; stepSize < FDoubleArray.Length; stepSize++)
				{
					double s = 0.0;
					
					while (!reader.Eos)
					{
						var buffer = new double[16];
						int n = reader.Read(buffer, startIndex, length, stepSize);
						for (int i = 0; i < startIndex; i++)
							Assert.AreEqual(0.0, buffer[i], "Step size was: {0}", stepSize);
						for (int i = startIndex; i < startIndex + n; i++)
						{
							Assert.AreEqual(s, buffer[i], "Step size was: {0}", stepSize);
							s += stepSize;
						}
						for (int i = startIndex + n; i < buffer.Length; i++)
							Assert.AreEqual(0.0, buffer[i], "Step size was: {0}", stepSize);
					}
					CheckArrayBoundaries();
					reader.Reset();
				}
			}
		}
		
		[Test]
		public void TestCyclicRead()
		{
			var stream = UnmanagedInStream.Create<double>(GetUnmanagedArray, Validate);
			
			using (var reader = stream.GetCyclicReader())
			{
				for (int bufferSize = 2; bufferSize < stream.Length * 5; bufferSize++)
				{
					var buffer = new double[bufferSize];
					
					int startIndex = 0;
					int length = buffer.Length;
					
					if (bufferSize > 5)
					{
						startIndex = 2;
						length = bufferSize - 2;
					}
					
					for (int stepSize = 8; stepSize < FDoubleArray.Length; stepSize++)
					{
						reader.Read(buffer, startIndex, length, stepSize);
						for (int i = 0; i < startIndex; i++)
							Assert.AreEqual(0.0, buffer[i], "Step size was: {0}, Buffer size was: {1}", stepSize, bufferSize);
						
						for (int i = startIndex; i < startIndex + length; i++)
						{
							try {
								Assert.AreEqual(FDoubleArray[FDoubleArrayBegin + (stepSize * (i - startIndex)) % stream.Length], buffer[i], "Step size was: {0}, Buffer size was: {1}", stepSize, bufferSize);
							} catch (Exception) {
								
								throw;
							}
						}
						
						for (int i = startIndex + length; i < buffer.Length; i++)
							Assert.AreEqual(0.0, buffer[i], "Step size was: {0}, Buffer size was: {1}", stepSize, bufferSize);
						
						CheckArrayBoundaries();
						reader.Reset();
					}
				}
			}
		}
		
		[Test]
		public void TestWrite()
		{
			var stream = UnmanagedOutStream.Create<double>(ResizeUnmanagedArray);
			
			var buffer = new double[16];
			using (var writer = stream.GetWriter())
			{
				for (int stepSize = 1; stepSize < FDoubleArray.Length; stepSize++)
				{
					while (!writer.Eos)
					{
						writer.Write((double) stepSize, stepSize);
					}
					CheckArrayBoundaries();
					writer.Reset();
				}
			}
		}
		
		[Test]
		public void TestBufferedWrite()
		{
			var stream = UnmanagedOutStream.Create<double>(ResizeUnmanagedArray);
			
			var buffer = new double[16];
			buffer[1] = 3.0;
			buffer[2] = 4.0;
			
			using (var writer = stream.GetWriter())
			{
				for (int stepSize = 1; stepSize < FDoubleArray.Length; stepSize++)
				{
					writer.Reset();
					
					int n = writer.Write(buffer, 1, 2, stepSize);
					CheckArrayBoundaries();
					
					writer.Reset();
					for (int i = 0; i < n; i++)
					{
						Assert.AreEqual(buffer[i + 1], FDoubleArray[FDoubleArrayBegin + i], "Step size was: {0}", stepSize);
					}
				}
			}
		}
	}
}
