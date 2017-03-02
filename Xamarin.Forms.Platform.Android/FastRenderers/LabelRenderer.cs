using System;
using System.ComponentModel;
using Android.Content.Res;
using Android.Graphics;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AView = Android.Views.View;

namespace Xamarin.Forms.Platform.Android.FastRenderers
{
	public class LabelRenderer : TextView, IVisualElementRenderer
	{
		int? _defaultLabelFor;
		bool _disposed;
		Label _element;
		ColorStateList _labelTextColorDefault;
		int _lastConstraintHeight;
		int _lastConstraintWidth;
		SizeRequest? _lastSizeRequest;
		float _lastTextSize = -1f;
		Typeface _lastTypeface;
		Color _lastUpdateColor = Color.Default;
		VisualElementPackager _visualElementPackager;
		VisualElementTracker _visualElementTracker;
		VisualElementRenderer _visualElementRenderer;
		AccessibilityThing _accessibilityThing;
		bool _wasFormatted;

		public LabelRenderer() : base(Forms.Context)
		{
			_labelTextColorDefault = TextColors;
		}

		public event EventHandler<VisualElementChangedEventArgs> ElementChanged;
		public event EventHandler<PropertyChangedEventArgs> ElementPropertyChanged;

		VisualElement IVisualElementRenderer.Element => Element;

		VisualElementTracker IVisualElementRenderer.Tracker => _visualElementTracker;

		AView IVisualElementRenderer.View => this;

		ViewGroup IVisualElementRenderer.ViewGroup => null;

		protected TextView Control => this;

		protected Label Element
		{
			get { return _element; }
			set
			{
				if (_element == value)
					return;

				Label oldElement = _element;
				_element = value;

				OnElementChanged(new ElementChangedEventArgs<Label>(oldElement, _element));

				if (_element != null)
					_element.SendViewInitialized(Control);
			}
		}

		SizeRequest IVisualElementRenderer.GetDesiredSize(int widthConstraint, int heightConstraint)
		{
			if (_lastSizeRequest.HasValue)
			{
				// if we are measuring the same thing, no need to waste the time
				bool canRecycleLast = widthConstraint == _lastConstraintWidth && heightConstraint == _lastConstraintHeight;

				if (!canRecycleLast)
				{
					// if the last time we measured the returned size was all around smaller than the passed constraint
					// and the constraint is bigger than the last size request, we can assume the newly measured size request
					// will not change either.
					int lastConstraintWidthSize = MeasureSpecFactory.GetSize(_lastConstraintWidth);
					int lastConstraintHeightSize = MeasureSpecFactory.GetSize(_lastConstraintHeight);

					int currentConstraintWidthSize = MeasureSpecFactory.GetSize(widthConstraint);
					int currentConstraintHeightSize = MeasureSpecFactory.GetSize(heightConstraint);

					bool lastWasSmallerThanConstraints = _lastSizeRequest.Value.Request.Width < lastConstraintWidthSize && _lastSizeRequest.Value.Request.Height < lastConstraintHeightSize;

					bool currentConstraintsBiggerThanLastRequest = currentConstraintWidthSize >= _lastSizeRequest.Value.Request.Width && currentConstraintHeightSize >= _lastSizeRequest.Value.Request.Height;

					canRecycleLast = lastWasSmallerThanConstraints && currentConstraintsBiggerThanLastRequest;
				}

				if (canRecycleLast)
					return _lastSizeRequest.Value;
			}

			Measure(widthConstraint, heightConstraint);
			SizeRequest result = new SizeRequest(new Size(MeasuredWidth, MeasuredHeight), new Size());
			result.Minimum = new Size(Math.Min(Context.ToPixels(10), result.Request.Width), result.Request.Height);

			_lastConstraintWidth = widthConstraint;
			_lastConstraintHeight = heightConstraint;
			_lastSizeRequest = result;

			return result;
		}

		void IVisualElementRenderer.SetElement(VisualElement element)
		{
			var label = element as Label;
			if (label == null)
				throw new ArgumentException("Element must be of type Label");

			Element = label;
			_accessibilityThing.SetAutomationId();
		}

		void IVisualElementRenderer.SetLabelFor(int? id)
		{
			if (_defaultLabelFor == null)
				_defaultLabelFor = LabelFor;

			LabelFor = (int)(id ?? _defaultLabelFor);
		}

		void IVisualElementRenderer.UpdateLayout()
		{
			VisualElementTracker tracker = _visualElementTracker;
			tracker?.UpdateLayout();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && !_disposed)
			{
				_disposed = true;

				if (_visualElementTracker != null)
				{
					_visualElementTracker.Dispose();
					_visualElementTracker = null;
				}

				if (_visualElementPackager != null)
				{
					_visualElementPackager.Dispose();
					_visualElementPackager = null;
				}

				if (_visualElementRenderer != null)
				{
					_visualElementRenderer.Dispose();
					_visualElementRenderer = null;
				}

				if (Element != null)
				{
					Element.PropertyChanged -= OnElementPropertyChanged;
				}
			}

			base.Dispose(disposing);
		}

		protected virtual void OnElementChanged(ElementChangedEventArgs<Label> e)
		{
			ElementChanged?.Invoke(this, new VisualElementChangedEventArgs(e.OldElement, e.NewElement));

			if (e.OldElement != null)
			{
				e.OldElement.PropertyChanged -= OnElementPropertyChanged;
			}

			if (e.NewElement != null)
			{
				if (_visualElementTracker == null)
				{
					_visualElementRenderer = new VisualElementRenderer(this);
					_accessibilityThing = new AccessibilityThing(this);
					_visualElementTracker = new VisualElementTracker(this);
					_visualElementPackager = new VisualElementPackager(this);
					_visualElementPackager.Load();
				}

				e.NewElement.PropertyChanged += OnElementPropertyChanged;

				(Control as FormsTextView)?.SkipNextInvalidate();
				UpdateText();
				if (e.OldElement?.LineBreakMode != e.NewElement.LineBreakMode)
					UpdateLineBreakMode();
				if (e.OldElement?.HorizontalTextAlignment != e.NewElement.HorizontalTextAlignment
				 || e.OldElement?.VerticalTextAlignment != e.NewElement.VerticalTextAlignment)
					UpdateGravity();
			}
		}

		void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			ElementPropertyChanged?.Invoke(this, e);

			if (e.PropertyName == Label.HorizontalTextAlignmentProperty.PropertyName || e.PropertyName == Label.VerticalTextAlignmentProperty.PropertyName)
				UpdateGravity();
			else if (e.PropertyName == Label.TextColorProperty.PropertyName)
				UpdateText();
			else if (e.PropertyName == Label.FontProperty.PropertyName)
				UpdateText();
			else if (e.PropertyName == Label.LineBreakModeProperty.PropertyName)
				UpdateLineBreakMode();
			else if (e.PropertyName == Label.TextProperty.PropertyName || e.PropertyName == Label.FormattedTextProperty.PropertyName)
				UpdateText();
		}

		void UpdateColor()
		{
			Color c = Element.TextColor;
			if (c == _lastUpdateColor)
				return;
			_lastUpdateColor = c;

			if (c.IsDefault)
				SetTextColor(_labelTextColorDefault);
			else
				SetTextColor(c.ToAndroid());
		}

		void UpdateFont()
		{
#pragma warning disable 618 // We will need to update this when .Font goes away
			Font f = Element.Font;
#pragma warning restore 618

			Typeface newTypeface = f.ToTypeface();
			if (newTypeface != _lastTypeface)
			{
				Typeface = newTypeface;
				_lastTypeface = newTypeface;
			}

			float newTextSize = f.ToScaledPixel();
			if (newTextSize != _lastTextSize)
			{
				SetTextSize(ComplexUnitType.Sp, newTextSize);
				_lastTextSize = newTextSize;
			}
		}

		void UpdateGravity()
		{
			Label label = Element;

			Gravity = label.HorizontalTextAlignment.ToHorizontalGravityFlags() | label.VerticalTextAlignment.ToVerticalGravityFlags();

			_lastSizeRequest = null;
		}

		void UpdateLineBreakMode()
		{
			SetSingleLine(false);
			switch (Element.LineBreakMode)
			{
				case LineBreakMode.NoWrap:
					SetMaxLines(1);
					Ellipsize = null;
					break;
				case LineBreakMode.WordWrap:
					Ellipsize = null;
					SetMaxLines(100);
					break;
				case LineBreakMode.CharacterWrap:
					Ellipsize = null;
					SetMaxLines(100);
					break;
				case LineBreakMode.HeadTruncation:
					SetMaxLines(1);
					Ellipsize = TextUtils.TruncateAt.Start;
					break;
				case LineBreakMode.TailTruncation:
					SetMaxLines(1);
					Ellipsize = TextUtils.TruncateAt.End;
					break;
				case LineBreakMode.MiddleTruncation:
					SetMaxLines(1);
					Ellipsize = TextUtils.TruncateAt.Middle;
					break;
			}
			_lastSizeRequest = null;
		}

		void UpdateText()
		{
			if (Element.FormattedText != null)
			{
				FormattedString formattedText = Element.FormattedText ?? Element.Text;
#pragma warning disable 618 // We will need to update this when .Font goes away
				TextFormatted = formattedText.ToAttributed(Element.Font, Element.TextColor, this);
#pragma warning restore 618
				_wasFormatted = true;
			}
			else
			{
				if (_wasFormatted)
				{
					SetTextColor(_labelTextColorDefault);
					_lastUpdateColor = Color.Default;
				}
				Text = Element.Text;
				UpdateColor();
				UpdateFont();

				_wasFormatted = false;
			}

			_lastSizeRequest = null;
		}
	}
}