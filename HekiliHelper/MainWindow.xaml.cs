﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Controls;
using Tesseract;
using HekiliHelper.Properties;
using System.Windows.Threading;
using HekiliHelper;
using System.Reflection.Emit;
using System.Diagnostics.Eventing.Reader;
using System.Linq;

namespace HekiliHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {


        private volatile string[] _currentKeyToSend = new string[] { string.Empty, string.Empty }; // Default key to send, can be changed dynamically
        //private volatile string _DetectedValueFirst = string.Empty;
        //private volatile string _DetectedValueSecond = string.Empty;

        private volatile bool keyProcessingFirst = false;
        private volatile bool keyProcessingSecond = false;


        private volatile bool activationKeyPressed = false;


        private volatile int[] _DetectedSameCount = new int[2] { 0, 0 };

        private static IntPtr _hookID = IntPtr.Zero;
        private static IntPtr _MouseHookID = IntPtr.Zero;
        private WindowsAPICalls.WindowsMessageProc _proc;
        private WindowsAPICalls.WindowsMessageProc _mouseProc;
        private IntPtr _wowWindowHandle = IntPtr.Zero;
        private CaptureScreen captureScreen;
        private ContinuousScreenCapture screenCapture;
        private ImageHelpers ImageHelpers = new ImageHelpers();

        private OcrModule ocr = new OcrModule();
        private MagnifierWindow magnifier;
        private MagnifierWindow magnifier2;
        private  ImageRegions CurrentImageRegions = new ImageRegions();
        private System.Windows.Threading.DispatcherTimer _timer;



        private int CurrentR = 25;
        private int CurrentG = 255;
        private int CurrentB = 255;
        private int CurrentA = 255;
        private int CurrentH = 255;
        private int CurrentS = 255;
        private int CurrentV = 255;
        private double CurrentThreshold = 0.3;
        private int CurrentCaptureRateMS = 100;
        private int CurrentKeyPressSpeedMS = 125;
        private int CurrentKeyDownDelayMS = 25;
        private Dispatcher mainWindowDispatcher;


        private volatile bool _keyPressMode = false;



        private IntPtr SetHookActionKey(WindowsAPICalls.WindowsMessageProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return WindowsAPICalls.SetWindowsHookEx(WindowsAPICalls.WH_KEYBOARD_LL, proc, WindowsAPICalls.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private bool AltPressed = false;
        private bool CtrlPressed = false;


        private IntPtr HookCallbackActionKey(int nCode, IntPtr wParam, IntPtr lParam)
        {
            bool handled = false;

            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);
                // We don't want to send key repeats if the app is not in focus
                if (!WindowsAPICalls.IsCurrentWindowWithTitle("World of Warcraft"))
                {
                    _timer.Stop();
      


                    // Let the key event go thru so the new focused app can handle it
                    keyProcessingFirst = false;
                    keyProcessingSecond = false;
                    activationKeyPressed = false;

                    handled = false;
                }
                else
                {
                    var item = ActivationKeyCodeMapper.GetVirtualKeyCode(Properties.Settings.Default.ActivationKey);
                    if (keyProcessingFirst == false || keyProcessingSecond == false)
                        if (wParam == (IntPtr)WindowsAPICalls.WM_KEYDOWN && (int)key == item)
                        {
                            // Find the window with the title "wow" only if we haven't already found it
                            if (_wowWindowHandle == IntPtr.Zero)
                            {
                                _wowWindowHandle = WindowsAPICalls.FindWindow(null, "wow");
                            }
                            if (_wowWindowHandle != IntPtr.Zero && !_timer.IsEnabled && keyProcessingFirst == false)  
                            {
                                activationKeyPressed = true;


                                _timer.Start();
                                mainTimerTick(this, new EventArgs());


      

                                // Don't let the message go thru.  this blocks the game from seeing the key press
                                handled = true;
                            }


                        }
                    if (wParam == (IntPtr)WindowsAPICalls.WM_KEYUP && (int)key == item)
                    {
                        activationKeyPressed = false;

                        _timer.Stop();
  
                        keyProcessingFirst = false;
                        keyProcessingSecond = false;
                        handled = true;
                    }
                    if (wParam == (IntPtr)WindowsAPICalls.WM_KEYDOWN && key == System.Windows.Input.Key.LeftCtrl)
                    {
                        CtrlPressed = true;
                    }
                    if (wParam == (IntPtr)WindowsAPICalls.WM_KEYDOWN && key == System.Windows.Input.Key.LeftAlt)
                    {
                        AltPressed = true;
                    }

                    if (wParam == (IntPtr)WindowsAPICalls.WM_KEYUP && key == System.Windows.Input.Key.LeftCtrl)
                    {
                        CtrlPressed = false;
                    }
                    if (wParam == (IntPtr)WindowsAPICalls.WM_KEYUP && key == System.Windows.Input.Key.LeftAlt)
                    {
                        AltPressed = false;
                    }


                }
            }


            // If the keypress has been handled, return a non-zero value.
            // Otherwise, call the next hook in the chain.
            // return handled ? (IntPtr)0:CallNextHookEx(_hookID, nCode, wParam, lParam); // Locks explorer
            return WindowsAPICalls.CallNextHookEx(_hookID, nCode, wParam, lParam); // Doesn't lock explorer
                                                                   //   return handled ? (IntPtr)1:CallNextHookEx(_hookID, nCode, wParam, lParam); // Blocks input to game does not block windowss

        }




        /// <summary>
        /// Takes a bitmap and converts it to an image that can be handled by WPF ImageBrush
        /// </summary>
        /// <param name="src">A bitmap image</param>
        /// <returns>The image as a BitmapImage for WPF</returns>
        public BitmapImage Convert(Bitmap src)
        {
            MemoryStream ms = new MemoryStream();
            ((System.Drawing.Bitmap)src).Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }

        private string OCRProcess(Bitmap b, System.Windows.Rect region)
        {
            string Result = "";
             //ocrResult;
            var ocrResult = ocr.PerformPointOcr(b, region);

            string s = ocrResult.Replace("\n", "");
            if (VirtualKeyCodeMapper.HasKey(s) && (!VirtualKeyCodeMapper.HasExcludeKey(s)))
            {
                var CurrentKeyToPress = StringExtensions.Extract(s, 4);
                if (!string.IsNullOrEmpty(CurrentKeyToPress.Trim()))
                {
                    Result = CurrentKeyToPress;
                }

            }
            return Result;

        }


        private string OCRProcess(Bitmap b)
        {

            string Result = "";
            OcrResult ocrResult;
            ocrResult = ocr.PerformFullOcr(b);

            string s = ocrResult.DetectedText.Replace("\n", "");
            if (VirtualKeyCodeMapper.HasKey(s) && (!VirtualKeyCodeMapper.HasExcludeKey(s)))
            {
                var CurrentKeyToPress = StringExtensions.Extract(s, 4);
                if (!string.IsNullOrEmpty(CurrentKeyToPress.Trim()))
                {
                    Result = CurrentKeyToPress;
                }

            }
            return Result;

        }


        //private void ProcessImageLocal(Bitmap image)
        //{
        //    // This only works with non HDR,  for now.

        //    Bitmap b = image;


        //    var origWidth = b.Width;
        //    var origHeight = b.Height;

        //    //Remember this is running in the background and every CPU cycle counts!!
        //    //This has to be FAST it is executing every 250 miliseconds 4 times a second
        //    //The faster this is the more times per second we can evaluate and react faster




        //    // It is expected that in the game the font on the hotkey text will be set to R:25 B:255 G:255 The font set to mica, and the size set to 40.
        //    // We filter out everying that isn't close to the color we want.
        //    // Doing it this way because it wwwas FAST.  This could be doing by doing a find conture and area but that takes alot more caculation than just a simple color filter

        //    b = ImageHelpers.FilterByColor(b, System.Drawing.Color.FromArgb(CurrentA, CurrentR, CurrentG, CurrentB), CurrentThreshold);
        //    b = ImageHelpers.RescaleImageToDpi(b, 300);
        //    //UpdateImageControl(Convert(b));
        //    // Bring the levels to somthing predictable, to simplify we convert it to greyscale
        //    b = ImageHelpers.ConvertToGrayscaleFast(b);
        //    b = ImageHelpers.BumpToBlack(b, 160);

        //    if (ImageHelpers.FindColorInFirstQuarter(b, System.Drawing.Color.White, CurrentThreshold))
        //    {
        //        b = ImageHelpers.BumpToWhite(b, 180);

        //        // For tesseract it doesn't like HUGE text so we bring it back down to the original size
        //        b = ImageHelpers.ResizeImage(b, origWidth, origHeight);

        //        // Bitmap DisplayImage = b;


        //        // Work Contourse later to find the main text and crop it out
        //        // Just leaving the code here  just incase I can come up with a fast way of doing this
        //        //var points = ImageHelpers.FindContours(b,128);
        //        //foreach (var contour in points)
        //        //{
        //        //    System.Console.WriteLine("Contour found with points:");
        //        //    var area = ImageHelpers.CalculateContourArea(contour);
        //        //    var BoundingRect = ImageHelpers.GetBoundingRect(contour);
        //        //    var ar = BoundingRect.Width / (float)(BoundingRect.Height);
        //        //    if (area > 200 & ar > .25 & ar < 1.2)
        //        //    {
        //        //        DisplayImage = ImageHelpers.DrawRectangle(b, BoundingRect, System.Drawing.Color.Red);
        //        //    }
        //        //}


        //        UpdateImageControl(Convert(b));

        //        string s = OCRProcess(b);
        //        lDetectedValue.Content = s;
        //    }
        //    else
        //    {
        //        // nothing found
        //        UpdateImageControl(Convert(_holderBitmap));
        //        lDetectedValue.Content = "";

        //    }

        //}





        private string ProcessImageOpenCV(Bitmap image, ref System.Windows.Controls.Label label, ref string _DetectedValue, ref int _DetectedSameCount,  ref System.Windows.Controls.Image DisplayControl, double Threshold, ref DetectionRegions regions)
        {
            var origWidth = image.Width;
            var origHeight = image.Height;
            var CurrentKeyToSend = string.Empty;
            int Rscale = ((int)(CurrentR * ((CurrentR * Threshold) / CurrentR)));
            int Gscale = ((int)(CurrentG * ((CurrentG * Threshold) / CurrentG)));
            int Bscale = ((int)(CurrentB * ((CurrentB * Threshold) / CurrentB)));


            string result = "";
            BitmapSource? OutImageSource;
            var CVMat = BitmapSourceConverter.ToMat(Convert(image));
            Mat resizedMat;


            resizedMat = ImageProcessingOpenCV.RescaleImageToNewDpi(CVMat, image.HorizontalResolution, 300);

            var IsolatedColor = ImageProcessingOpenCV.IsolateColorHSV(resizedMat, Scalar.FromRgb(CurrentR, CurrentG, CurrentB), Threshold);


            Mat gray = new Mat();
            Cv2.CvtColor(IsolatedColor, gray, ColorConversionCodes.BGR2GRAY);

            // Apply Otsu's thresholding
            Cv2.Threshold(gray, gray, 250, 255, ThresholdTypes.Otsu | ThresholdTypes.BinaryInv); //

            // Find the current bounding boxes, and try and get rid of the useless ones
            System.Windows.Rect[] ocrRegions = ocr.GetRegions(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(gray));
            List<System.Windows.Rect> usefulRegions = new List<System.Windows.Rect>();  
            if (ocrRegions.Length > 1)
            {
                for (int i = 0; i < ocrRegions.Length; i++)
                {


                    if (ocrRegions[i].Height * ocrRegions[i].Width < 1000)
                    {
                            ImageProcessingOpenCV.FillRectangle(ref gray, new OpenCvSharp.Rect((int)ocrRegions[i].X, (int)ocrRegions[i].Y, (int)ocrRegions[i].Width, (int)ocrRegions[i].Width),
                                Scalar.FromRgb(255, 255, 255)
                           //     Scalar.FromRgb(0, 0, 0)
                                );
                    } else
                    {
                        usefulRegions.Add(ocrRegions[i]);
                    }
                }
                    Task.Yield();
            } else
            {
                usefulRegions.Add(ocrRegions[0]);
            }

            var xMin = usefulRegions.Min(s => s.X);
            var yMin = usefulRegions.Min(s => s.Y);
            var xMax = usefulRegions.Max(s => s.X + s.Width);
            var yMax = usefulRegions.Max(s => s.Y + s.Height);
            var int32Rect = new Int32Rect((int)xMin, (int)yMin, (int)xMax - (int)xMin, (int)yMax - (int)yMin);



            System.Windows.Rect finalRegion = new System.Windows.Rect(int32Rect.X, int32Rect.Y, int32Rect.Width, int32Rect.Height);



            resizedMat = gray.Clone();
            resizedMat = ImageProcessingOpenCV.RescaleImageToNewDpi(resizedMat, image.HorizontalResolution, 96);

            regions.TopLeft = ImageProcessingOpenCV.IsThereAnImageInTopLeftQuarter(gray);
            regions.TopRight = ImageProcessingOpenCV.IsThereAnImageInTopRightQuarter(gray);
            regions.BottomLeft = ImageProcessingOpenCV.IsThereAnImageInBottomLeftQuarter(gray);
            regions.BottomCenter = ImageProcessingOpenCV.IsThereAnImageInBottomCenter(gray);
    

            if (regions.TopRight)
            {
                if (!regions.TopLeft && Properties.Settings.Default.QuickDecode == false)
                {
                    Cv2.CvtColor(resizedMat, resizedMat, ColorConversionCodes.BayerBG2RGB);
                    ImageProcessingOpenCV.DrawMarkers(ref resizedMat);

                    OutImageSource = BitmapSourceConverter.ToBitmapSource(resizedMat);
                    DisplayControl.Source = OutImageSource;
                    label.Content = "";
                    _DetectedValue = "";
                    result = "";
                    return result;
                }
                if (!regions.BottomLeft && Properties.Settings.Default.QuickDecode == true)
                {
                    Cv2.CvtColor(resizedMat, resizedMat, ColorConversionCodes.BayerBG2RGB);
                    ImageProcessingOpenCV.DrawMarkers(ref resizedMat);

                    OutImageSource = BitmapSourceConverter.ToBitmapSource(resizedMat);
                    DisplayControl.Source = OutImageSource;
                    label.Content = "";
                    _DetectedValue = "";
                    result = "";
                    return result;
                }


            }



            //  string s = OCRProcess(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(gray));
            string s = OCRProcess(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(gray), finalRegion);

            CurrentKeyToSend = s;
            Cv2.CvtColor(resizedMat, resizedMat, ColorConversionCodes.BayerBG2RGB);

            ImageProcessingOpenCV.DrawMarkers(ref resizedMat);

            OutImageSource = BitmapSourceConverter.ToBitmapSource(resizedMat);
            DisplayControl.Source = OutImageSource;

            if (_DetectedSameCount >= 2)
            {
                label.Content = s;
                _DetectedValue = s;
                _DetectedSameCount = 0;
            }
            else
            {
                if (label.Content.ToString() != s)
                {
                    lDetectedValue.Content = "";
                    _DetectedValue = "";
                }
                _DetectedSameCount++;
            }

            result = _DetectedValue;
            return result;
        }


        private void StartCaptureProcess()
        {
            // Define the area of the screen you want to capture
            int x = (int)magnifier.Left,
                y = (int)magnifier.Top,
                width = (int)magnifier.Width,
                height = (int)magnifier.Height;

            int x2 = (int)magnifier2.Left,
                y2 = (int)magnifier2.Top,
                width2 = (int)magnifier2.Width,
                height2 = (int)magnifier2.Height;


            // Initialize CaptureScreen with the dispatcher and the UI update action
            System.Windows.Rect[] regions = new System.Windows.Rect[2];
            regions[0] = new System.Windows.Rect { X = (double)x, Y = (double)y, Width = width, Height = height };
            regions[1] = new System.Windows.Rect { X = (double)x2, Y = (double)y2, Width = width2, Height = height2 };
            captureScreen = new CaptureScreen(regions, 0);
            //  image.Source = Convert(captureScreen.CapturedImage);

            // Create an instance of ContinuousScreenCapture with the CaptureScreen object
            screenCapture = new ContinuousScreenCapture(
                CurrentCaptureRateMS,
                Dispatcher,
                captureScreen
            );


            // Only process the 2nd image if it is active.  The image will still be captured behind the scenes,  but no OCR will be done on it.

            screenCapture.UpdateSecondImage += (Bitmap image) =>
            {
                if (Properties.Settings.Default.Use2ndImageDetection)
                {
                    //ProcessImageLocal(image);
                    double trasThreshold = CurrentThreshold == 0 ? 0.0 : CurrentThreshold / 100;
                    ProcessImageOpenCV(image, ref lDetectedValue2, ref  _currentKeyToSend[1], ref _DetectedSameCount[1], ref imageCap2, trasThreshold, ref CurrentImageRegions.SecondImageRegions);
                }
                else
                {
                    // Not capturing so set values back to 0-state
                    lDetectedValue2.Content = "";
                    _DetectedSameCount[1] = 0;
                    _currentKeyToSend[1] = "";
                }
            };

            // Assign a handler to the UpdateUIImage event
            screenCapture.UpdateFirstImage += (Bitmap image) =>
            {
                //ProcessImageLocal(image);
                double trasThreshold = CurrentThreshold == 0 ? 0.0 : CurrentThreshold / 100;
                ProcessImageOpenCV(image, ref lDetectedValue, ref _currentKeyToSend[0], ref _DetectedSameCount[0], ref imageCap, trasThreshold, ref CurrentImageRegions.FirstImageRegions);
            };


            


        }




        // Method to open the MagnifierWindow
        private void OpenMagnifierWindow()
        {
            magnifier.Show();
            magnifier2.Show();
        }

        string lastKey = "";
        private async void mainTimerTick(object? sender, EventArgs args)
        {
 
            _timer.Stop();
            // If key is already processing skip this tick
            if (keyProcessingFirst || keyProcessingSecond)
            {
                if (activationKeyPressed) _timer.Start();
                return;
            }
            if (_currentKeyToSend[0] == "" && keyProcessingFirst == true)
            {
                if (activationKeyPressed) _timer.Start();
                return;
            }


            if (CurrentImageRegions.FirstImageRegions.TopLeft == false && keyProcessingFirst == true)  // First Image is almost done processing
            {
                if (activationKeyPressed) _timer.Start();
                return;
            }

            if (_currentKeyToSend[0] == "")  // check again if the OCR is done if it isn't try again
            {
                keyProcessingFirst = false;
                if (activationKeyPressed) _timer.Start();
                return;
            }

            var keyToSendFirst = string.Empty;  
            var keyToSendSecond = string.Empty;
            


            keyToSendFirst = _currentKeyToSend[0];
            keyToSendSecond = _currentKeyToSend[1];



            // THis is a brute force way of trying to keep a key from being rapidly pressed

            if (VirtualKeyCodeMapper.HasExcludeKey(keyToSendFirst))
            {
                keyProcessingFirst = false;
                if (activationKeyPressed) _timer.Start();
                return;
            }

            // Check the key dictionary if the key is one we should handle
            if (!VirtualKeyCodeMapper.HasKey(keyToSendFirst))
            {
                keyProcessingFirst = false;
                if (activationKeyPressed) _timer.Start();
                return;
            }

            keyProcessingFirst = true;




            int vkCode = 0;


            if (_wowWindowHandle != nint.Zero)
            {
      

                //assuming we got here means we can do anything we want with the regions settings as they will update to the true values in the background
                //and we know what keys we want to send
                //CurrentImageRegions.FirstImageRegions.TopLeft = false;
                //CurrentImageRegions.FirstImageRegions.TopRight = false;
                //CurrentImageRegions.FirstImageRegions.BottomLeft = false;
                //CurrentImageRegions.FirstImageRegions.BottomCenter = false;



 

                ImageCapBorder.BorderBrush = System.Windows.Media.Brushes.Red;


                // I keep poking at this trying to figure out how to only send the key press again if a new key is to me pressed.
                // It fails if the next key to press is the same.
                // There would have to some logic in the capture to say its a new detection

                // Tranlate the char to the virtual Key Code
                vkCode = VirtualKeyCodeMapper.GetVirtualKeyCode(keyToSendFirst);

                // command is tied to CTRL or ALT So have to press them
                if (keyToSendFirst[0] == 'C') //&& CtrlPressed == false
                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, WindowsAPICalls.VK_CONTROL, 0);
                else
                    // Command isn't tied to CTRL so send a CTRL Up.
                    // This should really be peeking in the message buffer to see if the the key is really pressed or not. and only send the up if it is. 
                    // This could also be accomlished buy storing off the value in the message processor and storing a flag local if it saw one or not.
                    // keyboards are global so that may work.
                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_CONTROL, 0);  
                
                if (keyToSendFirst[0] == 'A') // && AltPressed == false
                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, WindowsAPICalls.VK_MENU, 0);
                else
                    // See Notes on CTRL.
                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_MENU, 0);

                // Press the command Key Down
                WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, vkCode, 0);
                
                
                // CTRL and ALT do not need to be held down just only pressed initally for the command to be interpeted correctly
                if (keyToSendFirst[0] == 'C' ) WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_CONTROL, 0); //&& CtrlPressed == true
                if (keyToSendFirst[0] == 'A' ) WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_MENU, 0); //&& AltPressed == true
                //     await Task.Delay((int)sliderCaptureRateMS.Value); // Give some time for hekili to refresh


                // I want to wait up to 500 MS to wait for the next command to atleast start no delay just yield to other threads.
                DateTime currentTime = DateTime.Now;
                while (_currentKeyToSend[0] != "")
                {
                    await Task.Delay(1);
                    if (DateTime.Now > currentTime.AddMilliseconds(250)) // Its been 1 second.  lets break out and try again. 
                    {
                        keyProcessingFirst = false;
                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, vkCode, 0);
                        if (activationKeyPressed) _timer.Start();
                        return;

                    }
                }



                if (_keyPressMode)
                {
                    while (CurrentImageRegions.FirstImageRegions.BottomLeft == false && button_Start.IsEnabled == false && activationKeyPressed == true)  // Do this loop till we have see we have a value starting to appear
                    {
                        await Task.Delay(1);
                    }
                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, vkCode, 0);

                    while (CurrentImageRegions.FirstImageRegions.TopLeft == false && button_Start.IsEnabled == false && activationKeyPressed == true)  // Do this loop till we have see we have a value starting to appear
                    {
           
                        await Task.Delay(1);
                 
                        // Lets explore some second options while this is on cooldown
                        if (Properties.Settings.Default.Use2ndImageDetection == true )
                        {
                
                            if (keyToSendSecond == "")
                                continue;  // We didn't have a value for the second key so skip


                            // This is to avoid duplicate keypresses.  not sure if blocking it is helpful or not, in theory it should just pop to the primary,
                            // but allowing it to press early should make it fire a little faster.   unsure...  skipping it avoids the question.  
                            //if (keyToSendSecond == keyToSendFirst)
                            //    continue;  // if its the same key let for first image handle it.





                            #region 2nd Key Options
                            DoSecondKeyAgain:
                            await Task.Delay(1);
                            keyProcessingSecond = true;




                            if (CurrentImageRegions.FirstImageRegions.BottomLeft == false)
                            {
                                keyProcessingSecond = false;
                                continue;
                            }
                            ImageCapBorder.BorderBrush = System.Windows.Media.Brushes.Black;
                            //   if (CurrentImageRegions.SecondImageRegions.TopLeft == false)
                            //{
                            //    keyProcessing2 = false;
                            //    _currentKeyToSend[1] = "";
                            //    continue;
                            //}

                            if ((!VirtualKeyCodeMapper.HasKey(keyToSendSecond)) || (VirtualKeyCodeMapper.HasExcludeKey(keyToSendSecond))
                            )
                            {
                                keyProcessingSecond = false;
                                continue;
                            }
                            ImageCap2Border.BorderBrush = System.Windows.Media.Brushes.Red;
                            //CurrentImageRegions.SecondImageRegions.TopLeft = false;
                            //CurrentImageRegions.SecondImageRegions.TopRight = false;
                            //CurrentImageRegions.SecondImageRegions.BottomLeft = false;
                            //CurrentImageRegions.SecondImageRegions.BottomCenter = false;
                            //_currentKeyToSend[1] = "";

                            int vkCode2 = 0;
                            if (_wowWindowHandle != nint.Zero)
                            {

             



                                // I keep poking at this trying to figure out how to only send the key press again if a new key is to me pressed.
                                // It fails if the next key to press is the same.
                                // There would have to some logic in the capture to say its a new detection


                                await Task.Delay(1);



                                if (_keyPressMode)
                                {
                                    //while (CurrentImageRegions.SecondImageRegions.TopRight == true && button_Start.IsEnabled == false) // delay our press till we make sure hekili has chosen a new cast
                                    //{
                                    //    await Task.Yield();
                                    //}

                             
                           

                                    while (CurrentImageRegions.FirstImageRegions.TopLeft == false && button_Start.IsEnabled == false ) // delay our press till we make sure hekili has chosen a new cast
                                    {
                                        await Task.Delay(1);
                                    }

                           
                                    keyToSendFirst = "";


                                    //await Task.Delay(30); // Give a little time for the keyup to be registered by the server.

                                    // Handle the if command is tied to CTRL or ALT
                                    if (keyToSendSecond[1] == 'C') //&& CtrlPressed == false
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, WindowsAPICalls.VK_CONTROL, 0);
                                    else
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_CONTROL, 0);

                                    if (keyToSendSecond[1] == 'A') //&& AltPressed == false
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, WindowsAPICalls.VK_MENU, 0);
                                    else
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_MENU, 0);


                                    // Tranlate the char to the virtual Key Code
                                    vkCode2 = VirtualKeyCodeMapper.GetVirtualKeyCode(keyToSendSecond);
                                
                                    if (activationKeyPressed == true)
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYDOWN, vkCode2, 0);



                                    // CTRL and ALT do not need to be held down just only pressed initally for the command to be interpeted correctly
                                    if (keyToSendSecond[1] == 'C') // && CtrlPressed == true
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_CONTROL, 0);

                                    if (keyToSendSecond[1] == 'A') // && AltPressed == true
                                        WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, WindowsAPICalls.VK_MENU, 0);
                                    // Now we pause until top is filled then we release the key that should queue the command.
                                    while (CurrentImageRegions.FirstImageRegions.TopLeft == false && button_Start.IsEnabled == false)
                                    {
                                        await Task.Delay(1);
                                    }
                                    WindowsAPICalls.PostMessage(_wowWindowHandle, WindowsAPICalls.WM_KEYUP, vkCode2, 0);
                                 
                                    if (_currentKeyToSend[1] != "" && _currentKeyToSend[1] == "")
                                    {
                                        keyToSendSecond = _currentKeyToSend[1];
                                        keyToSendFirst = "";
                                        goto DoSecondKeyAgain;
                                    }
                                 
                                }
                                // this stops the sending of the key till the timer is almost up.  
                                // it takes advantage of the cooldown visual cue in the game that darkens the font (changes the color)
                                // the OCR doesn't see a new char until it is almost times out, at that point it can be pressed and would be added to the action queue

                                ImageCap2Border.BorderBrush = System.Windows.Media.Brushes.Black;
                                keyProcessingSecond = false;


                                #endregion


                            }

                        }
                    }


                }
         
              
         



                // If where not watching for when things time out, we insert a hard delay
                if (!_keyPressMode)
                {
                    await Task.Delay(Random.Shared.Next() % 5 + CurrentKeyDownDelayMS);

                }

                // Let up on the command key

             
                keyProcessingFirst = false;
           

                // this stops the sending of the key till the timer is almost up.  
                // it takes advantage of the cooldown visual cue in the game that darkens the font (changes the color)
                // the OCR doesn't see a new char until it is almost times out, at that point it can be pressed and would be added to the action queue
                //_DetectedValueFirst = "";
                }

            

            ImageCapBorder.BorderBrush = System.Windows.Media.Brushes.Black;

            keyProcessingFirst = false;
            if (activationKeyPressed) _timer.Start();


        }



        //        Bitmap _holderBitmap;
        public MainWindow()
        {
            InitializeComponent();
            mainWindowDispatcher = this.Dispatcher;


            magnifier = new MagnifierWindow();
            magnifier.Left = Properties.Settings.Default.CapX > SystemParameters.PrimaryScreenWidth ? 100 : Properties.Settings.Default.CapX;
            magnifier.Top = Properties.Settings.Default.CapY > SystemParameters.PrimaryScreenHeight ? 100 : Properties.Settings.Default.CapY;
            magnifier.Width = Properties.Settings.Default.CapWidth;
            magnifier.Height = Properties.Settings.Default.CapHeight;
            magnifier.ShowInTaskbar = false;
            magnifier.SizeChanged += Magnifier_SizeChanged;
            magnifier.LocationChanged += Magnifier_LocationChanged;
       




            magnifier2 = new MagnifierWindow();
            magnifier2.border.BorderBrush = BorderBrush = System.Windows.Media.Brushes.Blue;
            magnifier2.Left = Properties.Settings.Default.CapX > SystemParameters.PrimaryScreenWidth ? 100 : Properties.Settings.Default.Cap2X;
            magnifier2.Top = Properties.Settings.Default.CapY > SystemParameters.PrimaryScreenHeight ? 100 : Properties.Settings.Default.Cap2Y;
            magnifier2.Width = Properties.Settings.Default.Cap2Width;
            magnifier2.Height = Properties.Settings.Default.Cap2Height;
            magnifier2.ShowInTaskbar = false;
            magnifier2.SizeChanged += Magnifier2_SizeChanged;
            magnifier2.LocationChanged += Magnifier2_LocationChanged;




            //CurrentR = Properties.Settings.Default.TargetR;
            //CurrentG = Properties.Settings.Default.TargetG;
            //CurrentB = Properties.Settings.Default.TargetB;
            //CurrentA = Properties.Settings.Default.TargetA;

            ColorPicker.PortableColorPicker cp;
            cp = (ColorPicker.PortableColorPicker)cbColorDruid.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.DruidTargetA, (byte)Properties.Settings.Default.DruidTargetR, (byte)Properties.Settings.Default.DruidTargetG, (byte)Properties.Settings.Default.DruidTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorPaladin.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.PaladinTargetA, (byte)Properties.Settings.Default.PaladinTargetR, (byte)Properties.Settings.Default.PaladinTargetG, (byte)Properties.Settings.Default.PaladinTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorWarlock.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.WarlockTargetA, (byte)Properties.Settings.Default.WarlockTargetR, (byte)Properties.Settings.Default.WarlockTargetG, (byte)Properties.Settings.Default.WarlockTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorShaman.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.ShamanTargetA, (byte)Properties.Settings.Default.ShamanTargetR, (byte)Properties.Settings.Default.ShamanTargetG, (byte)Properties.Settings.Default.ShamanTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorRogue.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.RogueTargetA, (byte)Properties.Settings.Default.RogueTargetR, (byte)Properties.Settings.Default.RogueTargetG, (byte)Properties.Settings.Default.RogueTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorWarrior.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.WarriorTargetA, (byte)Properties.Settings.Default.WarriorTargetR, (byte)Properties.Settings.Default.WarriorTargetG, (byte)Properties.Settings.Default.WarriorTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorEvoker.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.EvokerTargetA, (byte)Properties.Settings.Default.EvokerTargetR, (byte)Properties.Settings.Default.EvokerTargetG, (byte)Properties.Settings.Default.EvokerTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorHunter.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.HunterTargetA, (byte)Properties.Settings.Default.HunterTargetR, (byte)Properties.Settings.Default.HunterTargetG, (byte)Properties.Settings.Default.HunterTargetB);

            cp = (ColorPicker.PortableColorPicker)cbColorMage.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.MageTargetA, (byte)Properties.Settings.Default.MageTargetR, (byte)Properties.Settings.Default.MageTargetG, (byte)Properties.Settings.Default.MageTargetB);
            cp = (ColorPicker.PortableColorPicker)cbColorPriest.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.PriestTargetA, (byte)Properties.Settings.Default.PriestTargetR, (byte)Properties.Settings.Default.PriestTargetG, (byte)Properties.Settings.Default.PriestTargetB);
            cp = (ColorPicker.PortableColorPicker)cbColorMonk.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.MonkTargetA, (byte)Properties.Settings.Default.MonkTargetR, (byte)Properties.Settings.Default.MonkTargetG, (byte)Properties.Settings.Default.MonkTargetB);
            cp = (ColorPicker.PortableColorPicker)cbColorDemonHunter.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.DemonHunterTargetA, (byte)Properties.Settings.Default.DemonHunterTargetR, (byte)Properties.Settings.Default.DemonHunterTargetG, (byte)Properties.Settings.Default.DemonHunterTargetB);
            
            cp = (ColorPicker.PortableColorPicker)cbColorDefault.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.TargetA, (byte)Properties.Settings.Default.TargetR, (byte)Properties.Settings.Default.TargetG, (byte)Properties.Settings.Default.TargetB);



            RadioButton cb =  GetSelectedCheckBox();
            cp = (ColorPicker.PortableColorPicker)cb.Content;
            CurrentR = cp.SelectedColor.R;
            CurrentG = cp.SelectedColor.G;
            CurrentB = cp.SelectedColor.B;
            CurrentA = cp.SelectedColor.A;



            //TargetColorPicker.ColorState =  new ColorPicker.Models.ColorState();
            //TargetColorPicker.SelectedColor = System.Windows.Media.Color.FromArgb((byte)Properties.Settings.Default.TargetA, (byte)Properties.Settings.Default.TargetR, (byte)Properties.Settings.Default.TargetG, (byte)Properties.Settings.Default.TargetB);
            //CurrentR = Properties.Settings.Default.TargetR;
            //CurrentG = Properties.Settings.Default.TargetG;
            //CurrentB = Properties.Settings.Default.TargetB;
            //CurrentA = Properties.Settings.Default.TargetA;

            RadioButton rb = GetSelectedCheckBox();


            if ((string)rb.Tag == "default")
            {
                tbVariance.Text = Properties.Settings.Default.VariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.VariancePercent;
            }
            if ((string)rb.Tag == "druid")
            {
                tbVariance.Text = Properties.Settings.Default.DruidVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.DruidVariancePercent;
            }
            if ((string)rb.Tag == "paladin")
            {
                tbVariance.Text = Properties.Settings.Default.PaladinVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.PaladinVariancePercent;
            }
            if ((string)rb.Tag == "warlock")
            {
                tbVariance.Text = Properties.Settings.Default.WarlockVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.WarlockVariancePercent;
            }
            if ((string)rb.Tag == "shaman")
            {
                tbVariance.Text = Properties.Settings.Default.ShamanVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.ShamanVariancePercent;
            }
            if ((string)rb.Tag == "rogue")
            {
                tbVariance.Text = Properties.Settings.Default.RogueVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.RogueVariancePercent;
            }
            if ((string)rb.Tag == "warrior")
            {
                tbVariance.Text = Properties.Settings.Default.WarriorVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.WarriorVariancePercent;
            }
            if ((string)rb.Tag == "evoker")
            {
                tbVariance.Text = Properties.Settings.Default.EvokerVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.EvokerVariancePercent;
            }
            if ((string)rb.Tag == "hunter")
            {
                tbVariance.Text = Properties.Settings.Default.HunterVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.HunterVariancePercent;
            }
            if ((string)rb.Tag == "mage")
            {
                tbVariance.Text = Properties.Settings.Default.MageVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.MageVariancePercent;
            }
            if ((string)rb.Tag == "priest")
            {
                tbVariance.Text = Properties.Settings.Default.PriestVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.PriestVariancePercent;
            }
            if ((string)rb.Tag == "monk")
            {
                tbVariance.Text = Properties.Settings.Default.MonkVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.MonkVariancePercent;
            }
            if ((string)rb.Tag == "demonhunter")
            {
                tbVariance.Text = Properties.Settings.Default.DemonHunterVariancePercent.ToString();
                sliderColorVariancePercent.Value = Properties.Settings.Default.DemonHunterVariancePercent;
            }









            tbCaptureRateMS.Text = Properties.Settings.Default.CaptureRateMS.ToString();
            sliderCaptureRateMS.Value = Properties.Settings.Default.CaptureRateMS;

            tbKeyRateMS.Text = Properties.Settings.Default.KeyPressSpeedMS.ToString();
            sliderKeyRateMS.Value = Properties.Settings.Default.KeyPressSpeedMS;

            cbPushRelease.IsChecked = Properties.Settings.Default.PushAndRelease;
            cbQuickDecode.IsChecked = Properties.Settings.Default.QuickDecode;
            cbStayOnTop.IsChecked = Properties.Settings.Default.KeepOnTop;

            cbUse2ndImage.IsChecked = Properties.Settings.Default.Use2ndImageDetection;
            
            ImageCap2Border.Visibility = cbUse2ndImage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            lDetectedValue2.Visibility = ImageCap2Border.Visibility; // no need to reeval the vars, we already know.  (yeah this can be done in xaml bindings..  but right now I don't know how and don't feel like looking it up.)

            //Properties.Settings.Default.ActivationKey

            this.Topmost = Properties.Settings.Default.KeepOnTop;


            foreach (var x in cbActivationKey.Items)
            {

               if ( ((ComboBoxItem)x).Content.ToString() == Properties.Settings.Default.ActivationKey)
                    {
                    cbActivationKey.SelectedItem = x;
                }
            }

            OpenMagnifierWindow();

            this.Left = Properties.Settings.Default.AppStartX;
            this.Top = Properties.Settings.Default.AppStartY;

          
            
            _proc = HookCallbackActionKey;

            _mouseProc = MouseHookCallback;


            _wowWindowHandle = WindowsAPICalls.FindWindow(null, "World of Warcraft");

 
            StartCaptureProcess();


            // This timer handles the key sending
          
            _timer = new System.Windows.Threading.DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(25);
            _timer.Tick += mainTimerTick;






        }

  
        #region UI Event handlers
        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (cbStayOnTop.IsChecked == true)
            {
                this.Topmost = true;
                Properties.Settings.Default.KeepOnTop = true;
            }
            else
            {
                this.Topmost = false;
                Properties.Settings.Default.KeepOnTop = false;

            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Start the continuous capturing
            _wowWindowHandle = WindowsAPICalls.FindWindow(null, "World of Warcraft");
            if (_wowWindowHandle != IntPtr.Zero)
            {
                if (!screenCapture.IsCapturing)
                {
                    Magnifier_LocationChanged(sender, e);
                    Magnifier2_LocationChanged(sender, e);
                    screenCapture.StartCapture();

                    _hookID = _hookID == 0 ? SetHookActionKey(_proc) : 0; 
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = true;
                }
            }
 
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // ... When you want to stop capturing:
            if (screenCapture.IsCapturing)
            {
                screenCapture.StopCapture();
                if (_hookID == 0) {
                    WindowsAPICalls.UnhookWindowsHookEx(_hookID);
                    _hookID = 0;
                }
                button_Start.IsEnabled = true;
                button_Stop.IsEnabled = false;
            }
        }


        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            var filePath = ".\\captures\\Cap" + DateTime.Now.ToBinary().ToString() +".tif";


            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                BitmapEncoder encoder = new TiffBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create( ((BitmapImage)imageCap.Source) ));
                encoder.Save(fileStream);
            }
        }

        private void bToggleMagBorder_Click(object sender, RoutedEventArgs e)
        {
            if (magnifier.Visibility == Visibility.Visible)
            {
                magnifier.Visibility = Visibility.Hidden;
            }
        else
            {
                magnifier.Visibility = Visibility.Visible;
            }

            if (magnifier2.Visibility == Visibility.Visible)
            {
                magnifier2.Visibility = Visibility.Hidden;
            }
            else
            {
                magnifier2.Visibility = Visibility.Visible;
            }



        }

        private void Magnifier_LocationChanged(object? sender, EventArgs e)
        {
            //            if (screenCapture == null) return;
            //            screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;
            if (screenCapture == null) return;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;

                // Get the window's current location
                var left = magnifier.CurrrentLocationValue.X;
                var top = magnifier.CurrrentLocationValue.Y;
                var width = magnifier.CurrrentLocationValue.Width;
                var height = magnifier.CurrrentLocationValue.Height;

                // Adjust for DPI scaling
                var scaledLeft = left * dpiX;
                var scaledTop = top * dpiY;
                var scaledWidth = width * dpiX;
                var scaledHeight = height * dpiY - 15;
                //     if (screenCapture.CaptureRegion != null ) 
                screenCapture.CaptureRegion = 

                    new System.Windows.Rect[2] 
                    { new System.Windows.Rect(scaledLeft+1, scaledTop+1, scaledWidth-1, scaledHeight-1),
                      screenCapture.CaptureRegion == null ? new System.Windows.Rect() : screenCapture.CaptureRegion[1]
                     };
                //screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;

            }

        }

        private void Magnifier_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (screenCapture == null) return;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;

                // Get the window's current location
                var left = magnifier.CurrrentLocationValue.X;
                var top = magnifier.CurrrentLocationValue.Y;
                var width = magnifier.CurrrentLocationValue.Width;
                var height = magnifier.CurrrentLocationValue.Height;

                // Adjust for DPI scaling
                var scaledLeft = (left * dpiX) + 1;
                var scaledTop = (top * dpiY) + 1;
                var scaledWidth = (width * dpiX) - 1;
                var scaledHeight = (height * dpiY) - 15;

                scaledWidth = scaledWidth < 0 ? 1 : scaledWidth;
                scaledHeight = scaledHeight < 0 ? 1 : scaledHeight;


                //    if (screenCapture.CaptureRegion != null)
                screenCapture.CaptureRegion = 
                    new System.Windows.Rect[2]
                    { 
                        new System.Windows.Rect(scaledLeft+1, scaledTop+1, scaledWidth-1, scaledHeight-1),
                      screenCapture.CaptureRegion == null ? new System.Windows.Rect() : screenCapture.CaptureRegion[1]
                     };                //screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;

            }
        }

        private void Magnifier2_LocationChanged(object? sender, EventArgs e)
        {
            //            if (screenCapture == null) return;
            //            screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;
            if (screenCapture == null) return;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;

                // Get the window's current location
                var left = magnifier2.CurrrentLocationValue.X;
                var top = magnifier2.CurrrentLocationValue.Y;
                var width = magnifier2.CurrrentLocationValue.Width;
                var height = magnifier2.CurrrentLocationValue.Height;

                // Adjust for DPI scaling
                var scaledLeft = left * dpiX;
                var scaledTop = top * dpiY;
                var scaledWidth = width * dpiX;
                var scaledHeight = height * dpiY -15;
                // if (screenCapture.CaptureRegion != null)
                screenCapture.CaptureRegion = //new System.Windows.Rect(scaledLeft + 1, scaledTop + 1, scaledWidth - 1, scaledHeight - 1);
                    new System.Windows.Rect[2]
                    {
                      screenCapture.CaptureRegion == null ? new System.Windows.Rect() : screenCapture.CaptureRegion[0],
                     new System.Windows.Rect(scaledLeft+1, scaledTop+1, scaledWidth-1, scaledHeight-1)
                     };                //screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;

            }

        }

        private void Magnifier2_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (screenCapture == null) return;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;

                // Get the window's current location
                var left = magnifier2.CurrrentLocationValue.X;
                var top = magnifier2.CurrrentLocationValue.Y;
                var width = magnifier2.CurrrentLocationValue.Width;
                var height = magnifier2.CurrrentLocationValue.Height;

                // Adjust for DPI scaling
                var scaledLeft = (left * dpiX) + 1;
                var scaledTop = (top * dpiY) + 1;
                var scaledWidth = (width * dpiX) - 1;
                var scaledHeight = (height * dpiY) - 15;

                scaledWidth = scaledWidth < 0 ? 1 : scaledWidth;
                scaledHeight = scaledHeight < 0 ? 1 : scaledHeight;


                //     if (screenCapture.CaptureRegion != null)
                screenCapture.CaptureRegion = //new System.Windows.Rect(scaledLeft + 1, scaledTop + 1, scaledWidth - 1, scaledHeight - 1);
                    new System.Windows.Rect[2]
                    {
                      screenCapture.CaptureRegion == null ? new System.Windows.Rect() : screenCapture.CaptureRegion[0],
                     new System.Windows.Rect(scaledLeft+1, scaledTop+1, scaledWidth-1, scaledHeight-1)
                     };                //screenCapture.CaptureRegion = magnifier.CurrrentLocationValue;

            }
        }


        private void Window_Closed(object sender, EventArgs e)
        {



            Properties.Settings.Default.CapX = magnifier.Left;
            Properties.Settings.Default.CapY = magnifier.Top;
            Properties.Settings.Default.CapWidth = magnifier.Width;
            Properties.Settings.Default.CapHeight = magnifier.Height;
            Properties.Settings.Default.Cap2X = magnifier2.Left;
            Properties.Settings.Default.Cap2Y = magnifier2.Top;
            Properties.Settings.Default.Cap2Width = magnifier2.Width;
            Properties.Settings.Default.Cap2Height = magnifier2.Height;
            Properties.Settings.Default.AppStartX = this.Left;
            Properties.Settings.Default.AppStartY = this.Top;
            //Properties.Settings.Default.TargetR = CurrentR;
            //Properties.Settings.Default.TargetG = CurrentG;
            //Properties.Settings.Default.TargetB = CurrentB;
            //Properties.Settings.Default.TargetA = 255;

            Properties.Settings.Default.Save();

            magnifier.Close();
            magnifier2.Close();

            if (screenCapture.IsCapturing)
            {
                screenCapture.StopCapture();
            }
            if (_hookID != 0) {
                WindowsAPICalls.UnhookWindowsHookEx(_hookID);
            _hookID = 0;
            }
            
  

            if (_MouseHookID != IntPtr.Zero)
            {

                WindowsAPICalls.UnhookWindowsHookEx(_MouseHookID);
                _MouseHookID = IntPtr.Zero;
            }
            // Make sure we stop trapping the keyboard
            // UnhookWindowsHookEx(_hookID);
        }
        #endregion

 

        private RadioButton GetSelectedCheckBox ()
        {
            // Im tired so I'm just bruteforcing all of this.  Its not flexable and I know I will regert it later when a new one is added.
            if (cbColorDruid.IsChecked == true)
            {
                return cbColorDruid;
            }
            if (cbColorPaladin.IsChecked == true)
            {
                return cbColorPaladin;
            }
            if (cbColorWarlock.IsChecked == true)
            {
                return cbColorWarlock;
            }
            if (cbColorShaman.IsChecked == true)
            {
                return cbColorShaman;
            }
            if (cbColorRogue.IsChecked == true)
            {
                return cbColorRogue;
            }
            if (cbColorWarrior.IsChecked == true)
            {
                return cbColorWarrior;
            }
            if (cbColorEvoker.IsChecked == true)
            {
                return cbColorEvoker;
            }
            if (cbColorHunter.IsChecked == true)
            {
                return cbColorHunter;
            }
            if (cbColorMage.IsChecked == true)
            {
                return cbColorMage;
            }
            if (cbColorPriest.IsChecked == true)
            {
                return cbColorPriest;
            }
            if (cbColorMonk.IsChecked == true)
            {
                return cbColorMonk;
            }
            if (cbColorDemonHunter.IsChecked == true)
            {
                return cbColorDemonHunter;
            }
            cbColorDefault.IsChecked = true;
            cbColorDefault.Tag = "default";
            return cbColorDefault;
        }

        private void SetAssociatedSetting (RadioButton SelectedCheckbox, byte R, byte G, byte B, byte A)
        {
            RadioButton item = SelectedCheckbox;
            ColorPicker.PortableColorPicker cp = (ColorPicker.PortableColorPicker)item.Content;
            //cp.SelectedColor = System.Windows.Media.Color.FromArgb(R, G, B, A);
            cp.SelectedColor = System.Windows.Media.Color.FromArgb(A, R, G, B);



            if ((string)item.Tag == "default")
            {
                Settings.Default.TargetR = R;
                Settings.Default.TargetG = G;
                Settings.Default.TargetB = B;
                Settings.Default.TargetA = A;
            }
            if ((string)item.Tag == "druid")
            {
                Settings.Default.DruidTargetR = R;
                Settings.Default.DruidTargetG = G;
                Settings.Default.DruidTargetB = B;
                Settings.Default.DruidTargetA = A;
            }
            if ((string)item.Tag == "paladin") {
                Settings.Default.PaladinTargetR = R;
                Settings.Default.PaladinTargetG = G;
                Settings.Default.PaladinTargetB = B;
                Settings.Default.PaladinTargetA = A;
            }
            if ((string)item.Tag == "warlock")
            {
                Settings.Default.WarlockTargetR = R;
                Settings.Default.WarlockTargetG = G;
                Settings.Default.WarlockTargetB = B;
                Settings.Default.WarlockTargetA = A;
            }
            if ((string)item.Tag == "shaman")
            {
                Settings.Default.ShamanTargetR = R;
                Settings.Default.ShamanTargetG = G;
                Settings.Default.ShamanTargetB = B;
                Settings.Default.ShamanTargetA = A;
            }
            if ((string)item.Tag == "rogue")
            {
                Settings.Default.RogueTargetR = R;
                Settings.Default.RogueTargetG = G;
                Settings.Default.RogueTargetB = B;
                Settings.Default.RogueTargetA = A;
            }
            if ((string)item.Tag == "warrior")
            {
                Settings.Default.WarriorTargetR = R;
                Settings.Default.WarriorTargetG = G;
                Settings.Default.WarriorTargetB = B;
                Settings.Default.WarriorTargetA = A;
            }
            if ((string)item.Tag == "evoker")
            {
                Settings.Default.EvokerTargetR = R;
                Settings.Default.EvokerTargetG = G;
                Settings.Default.EvokerTargetB = B;
                Settings.Default.EvokerTargetA = A;
            }
            if ((string)item.Tag == "hunter")
            {
                Settings.Default.HunterTargetR = R;
                Settings.Default.HunterTargetG = G;
                Settings.Default.HunterTargetB = B;
                Settings.Default.HunterTargetA = A;
            }
            if ((string)item.Tag == "mage")
            {
                Settings.Default.MageTargetR = R;
                Settings.Default.MageTargetG = G;
                Settings.Default.MageTargetB = B;
                Settings.Default.MageTargetA = A;
            }
            if ((string)item.Tag == "priest")
            {
                Settings.Default.PriestTargetR = R;
                Settings.Default.PriestTargetG = G;
                Settings.Default.PriestTargetB = B;
                Settings.Default.PriestTargetA = A;
            }
            if ((string)item.Tag == "monk")
            {
                Settings.Default.MonkTargetR = R;
                Settings.Default.MonkTargetG = G;
                Settings.Default.MonkTargetB = B;
                Settings.Default.MonkTargetA = A;
            }
            if ((string)item.Tag == "demonhunter")
            {
                Settings.Default.DemonHunterTargetR = R;
                Settings.Default.DemonHunterTargetG = G;
                Settings.Default.DemonHunterTargetB = B;
                Settings.Default.DemonHunterTargetA = A;

            }
            CurrentR = R;
            CurrentG = G;
            CurrentB = B;

        




        }

        private void buPicker_Click(object sender, RoutedEventArgs e)
        {
            _MouseHookID = MouseSetHook(_mouseProc);
            RadioButton item = GetSelectedCheckBox();
            ColorPicker.PortableColorPicker cp = (ColorPicker.PortableColorPicker)item.Content;
            cp.SelectedColor = System.Windows.Media.Color.FromArgb(0, 0, 0, 0);
            // ((ColorPicker.PortableColorPicker)item.Content) = System.Windows.Media.Color.FromArgb(255, 0, 0, 0);
            //this.TargetColorPicker.SelectedColor = System.Windows.Media.Color.FromArgb(255, 0, 0, 0);

            // Other application logic
        }



        public static void ChangeCursor()
        {
            // Load the custom cursor
            IntPtr customCursor = WindowsAPICalls.LoadCursor(IntPtr.Zero, WindowsAPICalls.IDC_HAND);

            // Set the system cursor to the custom cursor
           // SetSystemCursor(customCursor, OCR_NORMAL);
        }

        public static void RestoreCursor()
        {
            // Load the default arrow cursor
            IntPtr defaultCursor = WindowsAPICalls.LoadCursor(IntPtr.Zero, 32512); // 32512 is the ID for the standard arrow

            // Restore the system cursor to the default
            WindowsAPICalls.SetSystemCursor(defaultCursor, WindowsAPICalls.OCR_NORMAL);
        }

        private static IntPtr MouseSetHook(WindowsAPICalls.WindowsMessageProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return WindowsAPICalls.SetWindowsHookEx(WindowsAPICalls.WH_MOUSE_LL, proc, WindowsAPICalls.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private  IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WindowsAPICalls.WM_LBUTTONDOWN)
            {
                WindowsAPICalls.MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<WindowsAPICalls.MSLLHOOKSTRUCT>(lParam);
                Console.WriteLine($"{hookStruct.pt.x}, {hookStruct.pt.y}");
                int x = hookStruct.pt.x;
                int y = hookStruct.pt.y;

                using (Bitmap bmp = new Bitmap(1, 1))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        // Copy the pixel's color into the bitmap
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
                    }

                    // Get the color of the pixel
                    System.Drawing.Color pixelColor = bmp.GetPixel(0, 0);

                    RadioButton item = GetSelectedCheckBox();
                    ColorPicker.PortableColorPicker cp = (ColorPicker.PortableColorPicker)item.Content;
                


                    // Convert System.Drawing.Color to System.Windows.Media.Color
                    SetAssociatedSetting(item, pixelColor.R, pixelColor.G, pixelColor.B, pixelColor.A);



                }





                WindowsAPICalls.UnhookWindowsHookEx(_MouseHookID);
                _MouseHookID = IntPtr.Zero;

            }
            return WindowsAPICalls.CallNextHookEx(_MouseHookID, nCode, wParam, lParam);
        }

        private void sliderColorVariance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
//            Properties.Settings.Default.VariancePercent = (int)sliderColorVariancePercent.Value;
            CurrentThreshold = (int)sliderColorVariancePercent.Value;
            if (tbVariance != null)
                tbVariance.Text = ((int)sliderColorVariancePercent.Value).ToString();

            RadioButton rb = GetSelectedCheckBox();


            if ((string)rb.Tag == "default")
            {
                Properties.Settings.Default.VariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "druid")
            {
                Properties.Settings.Default.DruidVariancePercent = (int)sliderColorVariancePercent.Value;

            }
            if ((string)rb.Tag == "paladin")
            {
                Properties.Settings.Default.PaladinVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "warlock")
            {
                Properties.Settings.Default.WarlockVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "shaman")
            {
                Properties.Settings.Default.ShamanVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "rogue")
            {
                Properties.Settings.Default.RogueVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "warrior")
            {
                Properties.Settings.Default.WarriorVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "evoker")
            {
                Properties.Settings.Default.EvokerVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "hunter")
            {
                Properties.Settings.Default.HunterVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "mage")
            {
                Properties.Settings.Default.MageVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "priest")
            {
                Properties.Settings.Default.PriestVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "monk")
            {
                Properties.Settings.Default.MonkVariancePercent = (int)sliderColorVariancePercent.Value;
            }
            if ((string)rb.Tag == "demonhunter")
            {
                Properties.Settings.Default.DemonHunterVariancePercent = (int)sliderColorVariancePercent.Value;
            }



        }

        private void sliderCaptureRateMS_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Properties.Settings.Default.CaptureRateMS = (int)sliderCaptureRateMS.Value;
            CurrentCaptureRateMS = (int)sliderCaptureRateMS.Value;
            if (tbCaptureRateMS != null)
            tbCaptureRateMS.Text = ((int)sliderCaptureRateMS.Value).ToString();
            if (screenCapture != null)
            screenCapture.CaptureInterval = (int)sliderCaptureRateMS.Value;
       
        }

        private void sliderKeyRateMS_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Properties.Settings.Default.KeyPressSpeedMS = (int)sliderKeyRateMS.Value;
            CurrentKeyDownDelayMS = (int)sliderKeyRateMS.Value;
            if (tbKeyRateMS != null)
            tbKeyRateMS.Text = ((int)sliderKeyRateMS.Value).ToString();

        }

        private new void PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            
            e.Handled = !StringExtensions.IsTextAllowed(e.Text);
        }

        private void TextBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(String)))
            {
                String text = (String)e.DataObject.GetData(typeof(String));
                if (!StringExtensions.IsTextAllowed(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void tbKeyRateMS_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            sliderKeyRateMS.Value = int.Parse(((System.Windows.Controls.TextBox)e.Source).Text.ToString());
        }

        private void tbCaptureRateMS_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            sliderCaptureRateMS.Value = int.Parse(((System.Windows.Controls.TextBox)e.Source).Text.ToString());
        }

        private void tbVariance_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            sliderColorVariancePercent.Value = int.Parse(((System.Windows.Controls.TextBox)e.Source).Text.ToString());
        }

        private void cbActivationKey_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.ActivationKey = ((ComboBoxItem)cbActivationKey.SelectedItem).Content.ToString();
        }

        private void bResetMagPosition_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.CapX = 50;
            Properties.Settings.Default.CapY = 50;
            Properties.Settings.Default.CapWidth = 100;
            Properties.Settings.Default.CapHeight = 100 ;

            magnifier.Left = Properties.Settings.Default.CapX > SystemParameters.PrimaryScreenWidth ? 100 : Properties.Settings.Default.CapX;
            magnifier.Top = Properties.Settings.Default.CapY > SystemParameters.PrimaryScreenHeight ? 100 : Properties.Settings.Default.CapY;
            magnifier.Width = Properties.Settings.Default.CapWidth;
            magnifier.Height = Properties.Settings.Default.CapHeight;

            magnifier2.Left = Properties.Settings.Default.Cap2X > SystemParameters.PrimaryScreenWidth ? 100 : Properties.Settings.Default.CapX;
            magnifier2.Top = Properties.Settings.Default.Cap2Y > SystemParameters.PrimaryScreenHeight ? 100 : Properties.Settings.Default.CapY;
            magnifier2.Width = Properties.Settings.Default.Cap2Width;
            magnifier2.Height = Properties.Settings.Default.Cap2Height;

        }

        private void cbPushRelease_Checked(object sender, RoutedEventArgs e)
        {

            _keyPressMode = true;
            Properties.Settings.Default.PushAndRelease = _keyPressMode;

        }

        private void cbPushRelease_Unchecked(object sender, RoutedEventArgs e)
        {
            _keyPressMode = false;
            Properties.Settings.Default.PushAndRelease = _keyPressMode;

        }

        private void cbQuickDecode_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.QuickDecode = true;
        }

        private void cbQuickDecode_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.QuickDecode = false;
        }

        private void TargetColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
          
            Properties.Settings.Default.TargetR = TargetColorPicker.SelectedColor.R;
            Properties.Settings.Default.TargetG = TargetColorPicker.SelectedColor.G; 
            Properties.Settings.Default.TargetB = TargetColorPicker.SelectedColor.B;
            Properties.Settings.Default.TargetA = TargetColorPicker.SelectedColor.A;
            CurrentR = TargetColorPicker.SelectedColor.R;
            CurrentG = TargetColorPicker.SelectedColor.G;
            CurrentB = TargetColorPicker.SelectedColor.B;
        }

        private void cbUse2ndImage_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Use2ndImageDetection = true;
            ImageCap2Border.Visibility = Visibility.Visible;
            lDetectedValue2.Visibility = Visibility.Visible;

        }

        private void cbUse2ndImage_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Use2ndImageDetection = false;
            ImageCap2Border.Visibility = Visibility.Collapsed;
            lDetectedValue2.Visibility = Visibility.Collapsed;

        }

        private CheckBox currentCheckbox;
        private void cbColorDruid_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton cb = (RadioButton)sender;
            if (cb.IsChecked is null) return;
            if (cb.Tag is null) return;

            ColorPicker.PortableColorPicker cp = (ColorPicker.PortableColorPicker)cb.Content;
            CurrentA = cp.SelectedColor.A;
            CurrentR = cp.SelectedColor.R;
            CurrentG = cp.SelectedColor.G;
            CurrentB = cp.SelectedColor.B;

            if ((string)cb.Tag == "default")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.VariancePercent;
            }
            if ((string)cb.Tag == "druid")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.DruidVariancePercent;

            }
            if ((string)cb.Tag == "paladin")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.PaladinVariancePercent;
            }
            if ((string)cb.Tag == "warlock")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.WarlockVariancePercent;
            }
            if ((string)cb.Tag == "shaman")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.ShamanVariancePercent;
            }
            if ((string)cb.Tag == "rogue")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.RogueVariancePercent;
            }
            if ((string)cb.Tag == "warrior")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.WarriorVariancePercent;
            }
            if ((string)cb.Tag == "evoker")
            {
                sliderColorVariancePercent.Value = (int)Properties.Settings.Default.EvokerVariancePercent;
            }
            if ((string)cb.Tag == "hunter")
            {
                sliderColorVariancePercent.Value= (int)Properties.Settings.Default.HunterVariancePercent;
            }
            if ((string)cb.Tag == "mage")
            {
                sliderColorVariancePercent.Value= (int)Properties.Settings.Default.MageVariancePercent;
            }
            if ((string)cb.Tag == "priest")
            {
                sliderColorVariancePercent.Value= (int)Properties.Settings.Default.PriestVariancePercent;
            }
            if ((string)cb.Tag == "monk")
            {
                sliderColorVariancePercent.Value= (int)Properties.Settings.Default.MonkVariancePercent;
            }
            if ((string)cb.Tag == "demonhunter")
            {
                sliderColorVariancePercent.Value= (int)Properties.Settings.Default.DemonHunterVariancePercent;
            }




        }


    }
}
