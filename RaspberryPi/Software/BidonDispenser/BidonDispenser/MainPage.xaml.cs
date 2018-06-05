﻿using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Threading;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.System.Profile;
using Windows.UI.ViewManagement;
using System.Collections.Generic;
using Windows.UI.Core;

namespace BidonDispenser {
    public sealed partial class MainPage: Page {
        private MainModel mainModel = new MainModel();

        private Boolean windowsIot = false;
        private int columnAmount = 0;

        //private Pn532Software nfcModule;
        private Pn532 nfcModule;
        private MicroController mc = null;

        public MainPage() {
            this.InitializeComponent();

            // Add the "unloadMainPage" function to the callbacks when the program is shutting down
            Unloaded += unloadMainPage;

            // Check on which device we're running
            System.Diagnostics.Debug.WriteLine("Running on "+AnalyticsInfo.VersionInfo.DeviceFamily);
            if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.IoT") {
                windowsIot = true;
            }

            initializePromotionTimer();
            
            if (windowsIot) {
                initButtons();
                mc = new MicroController();

                // Initialize the NFC module
                //nfcModule = new Pn532Software();
                //nfcModule = new Pn532(0);
                //nfcModule.setup();
                
            }
        }


        // Serial Test //////

        private void serialTest(object sender, RoutedEventArgs rea) {
            System.Diagnostics.Debug.WriteLine("Click: " + ((Button) sender).Name );

            if (!windowsIot)
                return;

            //////////////////////////////////////////////////////
            nfcModule.setup();
            return;
            //////////////////////////////////////////////////////

            String buttonFunction = ((Button) sender).Name;
            byte[] data;

            switch (buttonFunction) {
                case "Sense":               data = new byte[] { (byte) MicroController.Command.Sense, 0x00 };                   break;
                case "Lock":                data = new byte[] { (byte) MicroController.Command.Lock, 0x00 };                    break;
                case "Unlock":              data = new byte[] { (byte) MicroController.Command.Unlock, 0x00 };                  break;
                case "TemperatureCheck":    data = new byte[] { (byte) MicroController.Command.TemperatureCheck, 0x01, 0x03 };  break;
                case "Dispense":            data = new byte[] { (byte) MicroController.Command.Dispense, 0x01, 0x00 };          break;
                case "Distance":            data = new byte[] { (byte) MicroController.Command.Distance, 0x00 };                break;
                default: System.Diagnostics.Debug.WriteLine("Unknown button"); return;
            }

            while (!mc.serialInitialized);
            mc.transmitCommand(data);
            mc.waitForResponse();
        }


        // Buttons //////

        private Boolean buttonsDisabled = false;
        private readonly int[] BUTTON_PIN = { 20, 21, 26, 16, 19, 13, 12, 6 };
        private List<GpioPin> buttonPins = new List<GpioPin>();

        private void initButtons() {
            GpioController gpio = GpioController.GetDefault();

            if (gpio == null) {
                System.Diagnostics.Debug.WriteLine("There is no Gpio controller on this device");
                return;
            }
            
            for (int i = 0; i < BUTTON_PIN.Length; i++) {
                buttonPins.Add(gpio.OpenPin(BUTTON_PIN[i]));                                // Open all the button pins

                if (buttonPins[i].IsDriveModeSupported(GpioPinDriveMode.InputPullDown)) {   // Set the buttons' drive mode to input and pulldown (if supported)
                    buttonPins[i].SetDriveMode(GpioPinDriveMode.InputPullDown);
                } else {
                    buttonPins[i].SetDriveMode(GpioPinDriveMode.Input);
                }

                buttonPins[i].DebounceTimeout = TimeSpan.FromMilliseconds(50);              // Set the buttons' debouncetimeout
                buttonPins[i].ValueChanged += buttonValueHasChanged;                        // Configure which method has to be called when the button value has changed
            }

            System.Diagnostics.Debug.WriteLine("The buttons have been initialized");
        }

        private void buttonValueHasChanged(GpioPin sender, GpioPinValueChangedEventArgs e) {

            if (e.Edge == GpioPinEdge.RisingEdge) {

                if (buttonsDisabled) return;
                buttonsDisabled = true;

                int buttonNo = buttonPins.IndexOf(sender);
                System.Diagnostics.Debug.WriteLine("Button " + buttonNo + " has been pressed");

                switch (currentPanel) {

                    case uiPanel.pickColour:
                        // Update which colour has been selected
                        var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                            mainModel.selectedBottleColour = (MainModel.bottleColourName) buttonNo;
                        });
                        
                        // Show the "finishing up" panel
                        showFinishingUpPanel();
                        break;
                    
                    
                    case uiPanel.finishingUp:
                        // Cancel the operation if the user presses the left most button
                        if (buttonNo == 0)
                            showPickColourPanel();
                        break;
                    
                    
                    case uiPanel.thankYou:
                        // Return to the selection screen
                        stopThankYouTimer(null, null);
                        break;
                    
                    
                    default: break;
                }

                buttonsDisabled = false;
            }
        }


        // Misc Gpio //////

        private readonly int DOOR_PIN = 5;
        private GpioPin doorPin;                                    // High when the door is open
        
        private void initMiscGpio() {
            GpioController gpio = GpioController.GetDefault();

            if (gpio == null) {
                System.Diagnostics.Debug.WriteLine("There is no Gpio controller on this device");
                return;
            }

            doorPin = gpio.OpenPin(DOOR_PIN);

            // Set the doorpin's drive mode to input and pullup (if supported)
            if (doorPin.IsDriveModeSupported(GpioPinDriveMode.InputPullUp)) doorPin.SetDriveMode(GpioPinDriveMode.InputPullUp);
            else doorPin.SetDriveMode(GpioPinDriveMode.Input);

            doorPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
            doorPin.ValueChanged += doorValueHasChanged;

            System.Diagnostics.Debug.WriteLine("The misc gpio has been initialized");
        }

        private void doorValueHasChanged(GpioPin sender, GpioPinValueChangedEventArgs e) {
            GpioPinValue pinVal = doorPin.Read();

            if (pinVal == GpioPinValue.Low) {
                // TODO: Unlock the microcontroller
            } else {
                // TODO: Lock the microcontroller
            }
        }


        // Column Amount Selector Jumper //////

        private readonly int COLUMNSELECTOR_PIN = 23;
        
        private int howManyColumnsAreThere() {
            GpioController gpio = GpioController.GetDefault();

            if (gpio != null) {

                GpioPin columnSelectorPin = gpio.OpenPin(COLUMNSELECTOR_PIN);
                columnSelectorPin.SetDriveMode(GpioPinDriveMode.Input);

                GpioPinValue pinVal = columnSelectorPin.Read();

                if (pinVal == GpioPinValue.High)            // High = 4 columns
                    return 4;
                else if (pinVal == GpioPinValue.Low)        // Low = 8 columns
                    return 8;
                else
                    return 0;                               // Err

            } else {
                System.Diagnostics.Debug.WriteLine("There is no Gpio controller on this device");
            }

            return 0;
        }


        // Panel Show //////

        private enum uiPanel {
            pickColour, finishingUp, thankYou
        }
        private uiPanel currentPanel = uiPanel.pickColour;

        private void showCommandTestPanel() {
            var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                CommandTestPanel.Visibility = Visibility.Visible;
                PickColourPanel.Visibility = Visibility.Collapsed;
                FinishingUpPanel.Visibility = Visibility.Collapsed;
                ThankYouPanel.Visibility = Visibility.Collapsed;
                ThankYouFamPanel.Visibility = Visibility.Collapsed;
            });
        }

        private void showPickColourPanel() {
            currentPanel = uiPanel.pickColour;

            var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                CommandTestPanel.Visibility = Visibility.Collapsed;
                PickColourPanel.Visibility = Visibility.Visible;
                FinishingUpPanel.Visibility = Visibility.Collapsed;
                ThankYouPanel.Visibility = Visibility.Collapsed;
                ThankYouFamPanel.Visibility = Visibility.Collapsed;
            });
        }

        private void showFinishingUpPanel() {
            currentPanel = uiPanel.finishingUp;

            var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                CommandTestPanel.Visibility = Visibility.Collapsed;
                PickColourPanel.Visibility = Visibility.Collapsed;
                FinishingUpPanel.Visibility = Visibility.Visible;
                ThankYouPanel.Visibility = Visibility.Collapsed;
                ThankYouFamPanel.Visibility = Visibility.Collapsed;
            });
        }

        private void showThankYouPanel() {
            currentPanel = uiPanel.thankYou;

            var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                startThankYouTimer();
                CommandTestPanel.Visibility = Visibility.Collapsed;
                PickColourPanel.Visibility = Visibility.Collapsed;
                FinishingUpPanel.Visibility = Visibility.Collapsed;
                ThankYouPanel.Visibility = Visibility.Visible;
                ThankYouFamPanel.Visibility = Visibility.Collapsed;
            });
        }


        // Thank You Timer //////

        DispatcherTimer thankYouTimer;
        
        private void startThankYouTimer() {
            thankYouTimer = new DispatcherTimer();
            thankYouTimer.Interval = TimeSpan.FromSeconds(5);
            thankYouTimer.Tick += stopThankYouTimer;
            thankYouTimer.Start();
        }

        private void stopThankYouTimer(object sender, object e) {
            thankYouTimer.Stop();
            showPickColourPanel();
        }


        // Promotion Timer //////

        private const Double promotionMsPerTick = 200;
        private const int msUntilPromotionMediaSwitch = 30_000;

        private void initializePromotionTimer() {
            DispatcherTimer promotionTimer = new DispatcherTimer();
            promotionTimer.Interval = TimeSpan.FromMilliseconds(promotionMsPerTick);
            promotionTimer.Tick += promotionTimerTick;
            promotionTimer.Start();

            System.Diagnostics.Debug.WriteLine("The promotion timer has been initialized");
        }

        static int currentPromotionSource = 0;

        private void promotionTimerTick(object sender, object e) {
            if (mainModel.promotionTimerTickCounter >= msUntilPromotionMediaSwitch) {
                mainModel.promotionTimerTickCounter = 0;
            } else {
                mainModel.promotionTimerTickCounter += (int) promotionMsPerTick;
            }

            // This "preload" promotion source has been added to stop the screen from flickering when loading the next source
            if (mainModel.promotionTimerTickCounter == (msUntilPromotionMediaSwitch - 2000)) {
                currentPromotionSource = (currentPromotionSource + 1) % mainModel.promotionMedia.Count;         // Update which promotion source to show
                mainModel.promotionSourcePreload = (MainModel.promotionMediaName) (currentPromotionSource);     // Load the preload promotion source
            }

            if (mainModel.promotionTimerTickCounter >= msUntilPromotionMediaSwitch) {
                mainModel.promotionSource = (MainModel.promotionMediaName) (currentPromotionSource);            // Load the promotion source
            }
        }


        // Maintenance Timer //////

        private const int maintenanceMinutesPerTick = 30;

        private void initializeMaintenanceTimer() {
            DispatcherTimer mainenanceTimer = new DispatcherTimer();
            mainenanceTimer.Interval = TimeSpan.FromMinutes(maintenanceMinutesPerTick);
            mainenanceTimer.Tick += maintenanceTimerTick;
            mainenanceTimer.Start();

            System.Diagnostics.Debug.WriteLine("The maintenance timer has been initialized");
        }

        private void maintenanceTimerTick(object sender, object e) {

        }
        
        
        // Program shutdown //////

        private void unloadMainPage(object sender, object args) {
            nfcModule.dispose();
            mc.dispose();

            foreach (GpioPin button in buttonPins)
                button.Dispose();
        }

    }
}
