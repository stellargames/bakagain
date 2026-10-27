// LZMA SDK 26.05 by Igor Pavlov, public domain (https://www.7-zip.org/sdk.html).
// Vendored edits: namespace, internal, ICoder2/CoderPropID/ISetCoderProperties/IWriteCoderProperties removed.
// ICoder.h

using System;

namespace GameData.Setup.Lzma
{
	/// <summary>
	/// The exception that is thrown when an error in input stream occurs during decoding.
	/// </summary>
	internal class DataErrorException : ApplicationException
	{
		public DataErrorException(): base("Data Error") { }
	}

	/// <summary>
	/// The exception that is thrown when the value of an argument is outside the allowable range.
	/// </summary>
	internal class InvalidParamException : ApplicationException
	{
		public InvalidParamException(): base("Invalid Parameter") { }
	}

	internal interface ICodeProgress
	{
		/// <summary>
		/// Callback progress.
		/// </summary>
		/// <param name="inSize">
		/// input size. -1 if unknown.
		/// </param>
		/// <param name="outSize">
		/// output size. -1 if unknown.
		/// </param>
		void SetProgress(Int64 inSize, Int64 outSize);
	};

	internal interface ICoder
	{
		/// <summary>
		/// Codes streams.
		/// </summary>
		/// <param name="inStream">
		/// input Stream.
		/// </param>
		/// <param name="outStream">
		/// output Stream.
		/// </param>
		/// <param name="inSize">
		/// input Size. -1 if unknown.
		/// </param>
		/// <param name="outSize">
		/// output Size. -1 if unknown.
		/// </param>
		/// <param name="progress">
		/// callback progress reference.
		/// </param>
		/// <exception cref="DataErrorException">
		/// if input stream is not valid
		/// </exception>
		void Code(System.IO.Stream inStream, System.IO.Stream outStream,
			Int64 inSize, Int64 outSize, ICodeProgress progress);
	};

	internal interface ISetDecoderProperties
	{
		void SetDecoderProperties(byte[] properties);
	}
}
