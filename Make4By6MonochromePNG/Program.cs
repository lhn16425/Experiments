using System;
using System.IO;

namespace Make4By6MonochromePNG
{
	class Program
	{
		static void Main(string[] args)
		{
			//Util.Make4By6MonochromePNG("D:\\Downloads\\label-200.png", "D:\\Downloads\\label4x6-200.png");

			using (FileStream ofs = File.OpenWrite("D:\\Downloads\\label4x6-200.png"))
			{
				byte[] imageData = File.ReadAllBytes("D:\\Downloads\\label-200.png");
				string img = Util.Get4x6MonochromePNGImage(imageData);
				imageData = Convert.FromBase64String(img);
				ofs.Write(imageData, 0, imageData.Length);
			}
		}
	}
}
