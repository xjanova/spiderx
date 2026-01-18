// <copyright file="DashboardPage.xaml.cs" company="SpiderX">
// Copyright (c) SpiderX. All rights reserved.
// </copyright>

using SpiderX.App.ViewModels;

namespace SpiderX.App.Views;

/// <summary>
/// Dashboard page showing network status and quick actions.
/// </summary>
public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private bool _isAnimating;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardPage"/> class.
    /// </summary>
    /// <param name="viewModel">The dashboard view model.</param>
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
        StartAnimations();
    }

    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isAnimating = false;
    }

    private void StartAnimations()
    {
        _isAnimating = true;

        // Logo pulse animation
        AnimateLogoPulse();

        // Orb floating animations
        AnimateOrbs();

        // Status dot pulse
        AnimateStatusDot();
    }

    private async void AnimateLogoPulse()
    {
        while (_isAnimating)
        {
            await LogoBorder.ScaleTo(1.05, 1000, Easing.SinInOut);
            await LogoBorder.ScaleTo(1.0, 1000, Easing.SinInOut);
        }
    }

    private async void AnimateOrbs()
    {
        // Start all orb animations in parallel
        var orb1Task = AnimateOrbFloat(Orb1, 15, 3000);
        var orb2Task = AnimateOrbFloat(Orb2, -12, 4000);
        var orb3Task = AnimateOrbFloat(Orb3, 10, 3500);

        await Task.WhenAll(orb1Task, orb2Task, orb3Task);
    }

    private async Task AnimateOrbFloat(View orb, double distance, uint duration)
    {
        while (_isAnimating)
        {
            await orb.TranslateTo(0, distance, duration, Easing.SinInOut);
            await orb.TranslateTo(0, -distance, duration, Easing.SinInOut);
        }
    }

    private async void AnimateStatusDot()
    {
        while (_isAnimating)
        {
            await StatusDot.FadeTo(0.5, 800, Easing.SinInOut);
            await StatusDot.FadeTo(1.0, 800, Easing.SinInOut);
        }
    }
}
