﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Make4By6MonochromePNG
{
	// CopyToBpp code lifted as is from http://www.wischik.com/lu/programmer/1bpp.html
	public class Util
	{
		[DllImport("gdi32.dll")]
		public static extern bool DeleteObject(IntPtr hObject);

		[DllImport("user32.dll")]
		public static extern IntPtr GetDC(IntPtr hwnd);

		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("user32.dll")]
		public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

		[DllImport("gdi32.dll")]
		public static extern int DeleteDC(IntPtr hdc);

		[DllImport("gdi32.dll")]
		public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

		[DllImport("gdi32.dll")]
		public static extern int BitBlt(IntPtr hdcDst, int xDst, int yDst, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
		static int SRCCOPY = 0x00CC0020;

		[DllImport("gdi32.dll")]
		static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint Usage, out IntPtr bits, IntPtr hSection, uint dwOffset);
		static uint BI_RGB = 0;
		static uint DIB_RGB_COLORS = 0;
		[StructLayout(LayoutKind.Sequential)]
		public struct BITMAPINFO
		{
			public uint biSize;
			public int biWidth, biHeight;
			public short biPlanes, biBitCount;
			public uint biCompression, biSizeImage;
			public int biXPelsPerMeter, biYPelsPerMeter;
			public uint biClrUsed, biClrImportant;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
			public uint[] cols;
		}

		private static uint MAKERGB(int r, int g, int b)
		{
			return ((uint)(b & 255)) | ((uint)((r & 255) << 8)) | ((uint)((g & 255) << 16));
		}

		/// <summary>
		/// Copies a bitmap into a 1bpp/8bpp bitmap of the same dimensions, fast
		/// </summary>
		/// <param name="b">original bitmap</param>
		/// <param name="bpp">1 or 8, target bpp</param>
		/// <returns>a 1bpp copy of the bitmap</returns>
		private static Bitmap CopyToBpp(Bitmap b, int bpp)
		{
			if (bpp != 1 && bpp != 8) throw new System.ArgumentException("1 or 8", "bpp");

			// Plan: built into Windows GDI is the ability to convert
			// bitmaps from one format to another. Most of the time, this
			// job is actually done by the graphics hardware accelerator card
			// and so is extremely fast. The rest of the time, the job is done by
			// very fast native code.
			// We will call into this GDI functionality from C#. Our plan:
			// (1) Convert our Bitmap into a GDI hbitmap (ie. copy unmanaged->managed)
			// (2) Create a GDI monochrome hbitmap
			// (3) Use GDI "BitBlt" function to copy from hbitmap into monochrome (as above)
			// (4) Convert the monochrone hbitmap into a Bitmap (ie. copy unmanaged->managed)

			int w = b.Width, h = b.Height;
			IntPtr hbm = b.GetHbitmap(); // this is step (1)
										 //
										 // Step (2): create the monochrome bitmap.
										 // "BITMAPINFO" is an interop-struct which we define below.
										 // In GDI terms, it's a BITMAPHEADERINFO followed by an array of two RGBQUADs
			BITMAPINFO bmi = new BITMAPINFO();
			bmi.biSize = 40;  // the size of the BITMAPHEADERINFO struct
			bmi.biWidth = w;
			bmi.biHeight = h;
			bmi.biPlanes = 1; // "planes" are confusing. We always use just 1. Read MSDN for more info.
			bmi.biBitCount = (short)bpp; // ie. 1bpp or 8bpp
			bmi.biCompression = BI_RGB; // ie. the pixels in our RGBQUAD table are stored as RGBs, not palette indexes
			bmi.biSizeImage = (uint)(((w + 7) & 0xFFFFFFF8) * h / 8);
			bmi.biXPelsPerMeter = 1000000; // not really important
			bmi.biYPelsPerMeter = 1000000; // not really important
										   // Now for the colour table.
			uint ncols = (uint)1 << bpp; // 2 colours for 1bpp; 256 colours for 8bpp
			bmi.biClrUsed = ncols;
			bmi.biClrImportant = ncols;
			bmi.cols = new uint[256]; // The structure always has fixed size 256, even if we end up using fewer colours
			if (bpp == 1) { bmi.cols[0] = MAKERGB(0, 0, 0); bmi.cols[1] = MAKERGB(255, 255, 255); }
			else { for (int i = 0; i < ncols; i++) bmi.cols[i] = MAKERGB(i, i, i); }
			// For 8bpp we've created an palette with just greyscale colours.
			// You can set up any palette you want here. Here are some possibilities:
			// greyscale: for (int i=0; i<256; i++) bmi.cols[i]=MAKERGB(i,i,i);
			// rainbow: bmi.biClrUsed=216; bmi.biClrImportant=216; int[] colv=new int[6]{0,51,102,153,204,255};
			//          for (int i=0; i<216; i++) bmi.cols[i]=MAKERGB(colv[i/36],colv[(i/6)%6],colv[i%6]);
			// optimal: a difficult topic: http://en.wikipedia.org/wiki/Color_quantization
			// 
			// Now create the indexed bitmap "hbm0"
			IntPtr bits0; // not used for our purposes. It returns a pointer to the raw bits that make up the bitmap.
			IntPtr hbm0 = CreateDIBSection(IntPtr.Zero, ref bmi, DIB_RGB_COLORS, out bits0, IntPtr.Zero, 0);
			//
			// Step (3): use GDI's BitBlt function to copy from original hbitmap into monocrhome bitmap
			// GDI programming is kind of confusing... nb. The GDI equivalent of "Graphics" is called a "DC".
			IntPtr sdc = GetDC(IntPtr.Zero);       // First we obtain the DC for the screen
												   // Next, create a DC for the original hbitmap
			IntPtr hdc = CreateCompatibleDC(sdc); SelectObject(hdc, hbm);
			// and create a DC for the monochrome hbitmap
			IntPtr hdc0 = CreateCompatibleDC(sdc); SelectObject(hdc0, hbm0);
			// Now we can do the BitBlt:
			BitBlt(hdc0, 0, 0, w, h, hdc, 0, 0, SRCCOPY);
			// Step (4): convert this monochrome hbitmap back into a Bitmap:
			Bitmap b0 = Bitmap.FromHbitmap(hbm0);
			//
			// Finally some cleanup.
			DeleteDC(hdc);
			DeleteDC(hdc0);
			ReleaseDC(IntPtr.Zero, sdc);
			DeleteObject(hbm);
			DeleteObject(hbm0);
			//
			return b0;
		}

		// this function only works with shipping label image in PNG format
		public static string Get4x6MonochromePNGImage(byte[] imageData)
		{
			using (MemoryStream ims = new MemoryStream(imageData),
				   oms = new MemoryStream())
			{
				// this is the "4x6" PNG shipping label generated by Stamps.com.
				// It is not EXACTLY 4x6 so we must make it EXACTLY 4x6
				Bitmap bitmap = new Bitmap(ims);

				// convert the original shipping label to monochrome.
				// This step looks unecessary but without it, the end
				// result does not come out right.
				Bitmap monochrome = CopyToBpp(bitmap, 1);

				// create a new 4x6 bitmap
				Bitmap fourBySix = new Bitmap(4 * (int)Math.Ceiling(bitmap.HorizontalResolution), 6 * (int)Math.Ceiling(bitmap.VerticalResolution));
				Graphics graphics = Graphics.FromImage(fourBySix);

				// draw the monochrome shipping label on the new bitmap
				graphics.DrawImage(monochrome, 0, 0);

				// convert the new bitmap to 1-bit color depth again
				monochrome = CopyToBpp(fourBySix, 1);
				monochrome.Save(oms, ImageFormat.Png);

				// return the 4x6 monochrome PNG shipping label as a base64 string
				byte[] data = oms.ToArray();
				return Convert.ToBase64String(data);
			}
		}

		public static void Make4By6MonochromePNG(string sourcePngFile, string newPngFile)
		{
			Bitmap bitmap = new Bitmap(sourcePngFile);
			Console.WriteLine($"xDpi={bitmap.HorizontalResolution} yDpi={bitmap.VerticalResolution}");

			Bitmap monochrome = CopyToBpp(bitmap, 1);
			Console.WriteLine($"xDpi={monochrome.HorizontalResolution} yDpi={monochrome.VerticalResolution}");

			Bitmap fourBySix = new Bitmap(4 * (int)Math.Ceiling(bitmap.HorizontalResolution), 6 * (int)Math.Ceiling(bitmap.VerticalResolution));
			Graphics graphics = Graphics.FromImage(fourBySix);
			graphics.DrawImage(monochrome, 0, 0);

			monochrome = CopyToBpp(fourBySix, 1);
			Console.WriteLine($"xDpi={monochrome.HorizontalResolution} yDpi={monochrome.VerticalResolution}");
			monochrome.Save(newPngFile, ImageFormat.Png);

			///fourBySix.Save(newPngFile, ImageFormat.Png);

			//Bitmap bitmap = new Bitmap(sourcePngFile);
			////Bitmap monochrome = CopyToBpp(bitmap, 1);
			//Console.WriteLine($"xDpi={bitmap.HorizontalResolution} yDpi={bitmap.VerticalResolution}");
			//Bitmap fourBySix = new Bitmap(4 * (int)Math.Ceiling(bitmap.HorizontalResolution), 6 * (int)Math.Ceiling(bitmap.VerticalResolution));
			//Graphics graphics = Graphics.FromImage(fourBySix);
			////graphics.DrawImage(monochrome, 0, 0);
			//graphics.DrawImageUnscaled(bitmap, 0, 0);
			//Bitmap monochrome = CopyToBpp(fourBySix, 1);
			//Console.WriteLine($"xDpi={monochrome.HorizontalResolution} yDpi={monochrome.VerticalResolution}");
			//monochrome.Save(newPngFile, ImageFormat.Png);
			////fourBySix.Save(newPngFile, ImageFormat.Png);
		}
	}
}